using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Runs;
using RunReplays;
using RunReplays.Commands;

namespace STS2Twitch;

[ModInitializer("Initialize")]
public class Plugin
{
    private static TwitchIrcClient? _ircClient;
    private static VoteExecutioner? _voteExecutioner;
    private static bool _signalConnected;

    public static void Initialize()
    {
        var harmony = new Harmony("com.boardengineer.twitchvotecontroller");

        try
        {
            harmony.PatchAll(typeof(Plugin).Assembly);
            RunStartHelper.PatchOverlay(harmony);
            GD.Print("[TwitchVoteController] Harmony patches applied.");
        }
        catch (Exception ex)
        {
            GD.Print($"[TwitchVoteController] Harmony patch error: {ex.Message}");
        }

        var assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        GD.Print($"[TwitchVoteController] Assembly location: {assemblyDir}");
        var configPath = Path.Combine(assemblyDir ?? ".", "TwitchVoteController.config.json");
        GD.Print($"[TwitchVoteController] Looking for config at: {configPath}");

        var config = TwitchConfig.Load(configPath);
        if (config == null)
        {
            GD.Print("[TwitchVoteController] Config not loaded, skipping IRC connection.");
            return;
        }

        _ircClient = new TwitchIrcClient(config);
        _voteExecutioner = new VoteExecutioner();
        _ircClient.OnMessageReceived += (username, message) =>
        {
            PlayerActionBuffer.LogMigrationWarning($"[Chat] {username}: {message}");

            var cmdResponse = ChatCommands.TryHandle(message);
            if (cmdResponse != null)
            {
                _ircClient.SendMessage(cmdResponse);
                return;
            }

            _voteExecutioner.OnChatMessage(username, message);
        };
        _ircClient.Start();
        GD.Print("[TwitchVoteController] IRC client started.");
    }

    public static void SetupVoteExecutioner(Node parent)
    {
        _voteExecutioner?.Initialize(_ircClient!, parent);
    }

    public static void TryConnectSignal()
    {
        if (_signalConnected || _ircClient == null)
            return;

        var emitter = ReplayDispatcher.Emitter;
        if (emitter == null || !GodotObject.IsInstanceValid((GodotObject)(object)emitter))
            return;

        ((GodotObject)emitter).Connect(
            "InputRequired",
            Callable.From((Action)OnInputRequired),
            0u);

        _signalConnected = true;
        GD.Print("[TwitchVoteController] Connected to RunReplays InputRequired signal.");
    }

    private static void OnInputRequired()
    {
        PlayerActionBuffer.LogMigrationWarning("[TwitchVoteController] InputRequired signal received.");
        StartOrRestartVote();
    }

    public static void StartOrRestartVote()
    {
        if (_voteExecutioner == null)
            return;

        var commands = ReplayDispatcher.GetAvailableCommands();
        if (commands == null || commands.Count == 0)
            return;

        if (_voteExecutioner.AwaitingProceedAfterShop)
        {
            _voteExecutioner.AwaitingProceedAfterShop = false;
            var proceedCmd = commands.FirstOrDefault(c => c is ProceedToMapCommand);
            if (proceedCmd != null)
            {
                _voteExecutioner.StartVote(new List<ReplayCommand> { proceedCmd });
                return;
            }
        }

        var hasMap = commands.Any(c => c is MapMoveCommand);
        var hasProceed = commands.Any(c => c is ProceedToMapCommand);

        var hasTakeCard = commands.Any(c => c is TakeCardCommand);

        List<ReplayCommand> filtered;
        if (_voteExecutioner.AwaitingMapMove && hasMap)
            filtered = commands.Where(c => c is MapMoveCommand).ToList();
        else if (hasMap && hasProceed)
            filtered = commands.Where(c => c is not MapMoveCommand).ToList();
        else
            filtered = commands.ToList();

        if (hasTakeCard)
            filtered = filtered.Where(c => c is not ClaimRewardCommand).ToList();

        if (filtered.Any(c => c is ClickGridCardCommand))
            filtered = filtered.Where(c => c is not SelectGridCardCommand and not ChooseRestSiteOptionCommand).ToList();

        if (filtered.Any(c => c is CancelGridSelectionCommand) && filtered.Any(c => c is ConfirmGridSelectionCommand))
            filtered = filtered.Where(c => c is CancelGridSelectionCommand or ConfirmGridSelectionCommand).ToList();

        // Shop state machine
        var hasOpenShop = filtered.Any(c => c is OpenShopCommand or OpenFakeShopCommand);
        var hasBuy = filtered.Any(c => c is BuyCardCommand or BuyRelicCommand
            or BuyPotionCommand or BuyCardRemovalCommand);

        if (_voteExecutioner.ShopOpened)
            filtered = filtered.Where(c => c is not OpenShopCommand and not OpenFakeShopCommand and not ProceedToMapCommand).ToList();
        else if (hasOpenShop && hasBuy)
            filtered = filtered.Where(c => c is not BuyCardCommand and not BuyRelicCommand
                and not BuyPotionCommand and not BuyCardRemovalCommand and not MapMoveCommand).ToList();

        if (!hasOpenShop && !hasBuy)
            _voteExecutioner.ShopOpened = false;

        // Treasure room state machine
        var hasOpenChest = filtered.Any(c => c is OpenChestCommand);
        var hasTakeRelic = filtered.Any(c => c is TakeChestRelicCommand);

        if (_voteExecutioner.TreasureState == VoteExecutioner.ChestState.RelicTaken)
            filtered = filtered.Where(c => c is not OpenChestCommand and not TakeChestRelicCommand).ToList();
        else if (_voteExecutioner.TreasureState == VoteExecutioner.ChestState.Opened)
            filtered = filtered.Where(c => c is not OpenChestCommand and not ProceedToMapCommand).ToList();
        else if (hasOpenChest && hasTakeRelic)
            filtered = filtered.Where(c => c is not TakeChestRelicCommand and not ProceedToMapCommand).ToList();

        if (!hasOpenChest && !hasTakeRelic)
            _voteExecutioner.TreasureState = VoteExecutioner.ChestState.Closed;

        if (!hasMap)
            _voteExecutioner.AwaitingMapMove = false;

        if (filtered.Count == 0)
            return;

        CombatOverlay.Refresh();

        var sorted = filtered
            .OrderBy(c => CommandDescriber.GetSortKey(c))
            .ToList();
        _voteExecutioner.StartVote(sorted);
    }
}

[HarmonyPatch(typeof(NGame), "LoadMainMenu")]
public class MainMenuPatch
{
    [HarmonyPostfix]
    public static void Postfix(NGame __instance)
    {
        Plugin.SetupVoteExecutioner(__instance);
        Plugin.TryConnectSignal();
    }
}

[HarmonyPatch(typeof(RunManager), nameof(RunManager.SetUpNewSinglePlayer))]
public class NewRunStartPatch
{
    [HarmonyPrefix]
    public static void Prefix()
    {
        RunStartHelper.Activate();
    }
}

[HarmonyPatch(typeof(RunManager), nameof(RunManager.SetUpSavedSinglePlayer))]
public class SavedRunStartPatch
{
    [HarmonyPrefix]
    public static void Prefix()
    {
        RunStartHelper.Activate();
    }
}

static class RunStartHelper
{
    private static readonly FieldInfo? ReplayActiveField =
        typeof(ReplayEngine).GetField("_replayActive", BindingFlags.Static | BindingFlags.NonPublic);

    private static readonly Type? RunOverlayType =
        typeof(ReplayEngine).Assembly.GetType("RunReplays.RunOverlay");

    private static readonly PropertyInfo? OverlayVisibleProp =
        RunOverlayType?.GetProperty("OverlayVisible", BindingFlags.Static | BindingFlags.NonPublic);

    public static void Activate()
    {
        ReplayActiveField?.SetValue(null, true);
        ReplayDispatcher.GameSpeed = 1.0f;
        HideRunOverlay();
        GD.Print("[TwitchVoteController] ReplayActive set before run start.");
    }

    public static void HideRunOverlay()
    {
        OverlayVisibleProp?.SetValue(null, false);
    }

    public static void PatchOverlay(Harmony harmony)
    {
        if (RunOverlayType == null) return;

        var initMethod = RunOverlayType.GetMethod("InitForRun", BindingFlags.Static | BindingFlags.NonPublic);
        if (initMethod == null) return;

        var postfix = typeof(RunStartHelper).GetMethod(nameof(InitForRunPostfix), BindingFlags.Static | BindingFlags.Public);
        harmony.Patch(initMethod, postfix: new HarmonyMethod(postfix));
    }

    public static void InitForRunPostfix()
    {
        // RunOverlay.InitForRun sets OverlayVisible = ReplayEngine.IsActive.
        // We need to re-hide it after it rebuilds on the main thread.
        Callable.From(HideRunOverlay).CallDeferred();
    }
}
