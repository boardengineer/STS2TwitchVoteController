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
using STS2Twitch.Overlays;

namespace STS2Twitch;

[ModInitializer("Initialize")]
public class Plugin
{
    private static TwitchIrcClient? _ircClient;
    private static VoteExecutioner? _voteExecutioner;
    private static CharacterVoteController? _charVoteController;
    internal static bool _signalConnected;
    internal static bool _pendingCharacterVote;

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

        var configDir = Path.Combine(
            System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData),
            "SlayTheSpire2");
        Directory.CreateDirectory(configDir);
        var configPath = Path.Combine(configDir, "TwitchVoteController.config.json");
        GD.Print($"[TwitchVoteController] Looking for config at: {configPath}");

        var config = TwitchConfig.Load(configPath);
        if (config == null)
        {
            GD.Print("[TwitchVoteController] Config not loaded, skipping IRC connection.");
            return;
        }

        _ircClient = new TwitchIrcClient(config);
        _voteExecutioner = new VoteExecutioner();
        _charVoteController = new CharacterVoteController();
        _ircClient.OnMessageReceived += (username, message) =>
        {
            GD.Print($"[Chat] {username}: {message}");

            var cmdResponse = ChatCommands.TryHandle(message);
            if (cmdResponse != null)
            {
                _ircClient.SendMessage(cmdResponse);
                return;
            }

            _voteExecutioner.OnChatMessage(username, message);
            _charVoteController.OnChatMessage(username, message);
        };
        _ircClient.Start();
        GD.Print("[TwitchVoteController] IRC client started.");
    }

    public static void SetupVoteExecutioner(NGame game)
    {
        GD.Print("[TwitchVoteController] SetupVoteExecutioner called.");
        _voteExecutioner?.Initialize(_ircClient!, game);
        _charVoteController?.Initialize(_ircClient!, game);

        if (_watchdogTimer != null && GodotObject.IsInstanceValid(_watchdogTimer))
            _watchdogTimer.QueueFree();

        _watchdogTimer = new Timer();
        _watchdogTimer.WaitTime = 5.0;
        _watchdogTimer.Autostart = true;
        _watchdogTimer.Timeout += OnWatchdogTick;
        game.AddChild(_watchdogTimer);
        GD.Print("[TwitchVoteController] Watchdog timer started (5s interval).");
    }

    private static Timer? _watchdogTimer;

    private static void OnWatchdogTick()
    {
        var emitter = ReplayDispatcher.Emitter;
        var emitterValid = emitter != null && GodotObject.IsInstanceValid((GodotObject)(object)emitter);

        List<ReplayCommand>? commands = null;
        bool commandsFailed = false;
        string commandSummary;
        try
        {
            commands = ReplayDispatcher.GetAvailableCommands();
            commandSummary = commands == null || commands.Count == 0
                ? "none"
                : string.Join(", ", commands.Select(c => c.GetType().Name));
        }
        catch (Exception ex)
        {
            commandsFailed = true;
            commandSummary = $"ERROR: {ex.GetType().Name}: {ex.Message}";
        }

        var voteActive = _voteExecutioner != null
            ? (bool?)typeof(VoteExecutioner)
                .GetField("_voteActive", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                ?.GetValue(_voteExecutioner)
            : null;
        var charVoteActive = _charVoteController?.IsVoteActive;

        GD.Print(
            $"[Watchdog] signalConnected={_signalConnected} | emitterValid={emitterValid} | " +
            $"voteActive={voteActive} | charVoteActive={charVoteActive} | " +
            $"availableCommands=[{commandSummary}]");

        var anyVoteActive = (voteActive == true) || (charVoteActive == true);
        if (!anyVoteActive && _signalConnected && emitterValid)
        {
            if (commands != null && commands.Count > 0)
            {
                GD.Print("[Watchdog] No active vote but commands available — recovering.");
                StartOrRestartVote();
            }
            else if (commandsFailed)
            {
                GD.Print("[Watchdog] No active vote and GetAvailableCommands threw — re-activating and retrying.");
                RunStartHelper.Activate();
                StartOrRestartVote();
            }
        }
    }

    public static void TryConnectSignal()
    {
        if (_signalConnected)
        {
            var emitter = ReplayDispatcher.Emitter;
            var emitterValid = emitter != null && GodotObject.IsInstanceValid((GodotObject)(object)emitter);
            GD.Print(
                $"[TwitchVoteController] TryConnectSignal: already connected (emitterValid={emitterValid}), skipping.");
            return;
        }

        if (_ircClient == null)
        {
            GD.Print("[TwitchVoteController] TryConnectSignal: IRC client is null (config missing?).");
            return;
        }

        var emitterObj = ReplayDispatcher.Emitter;
        if (emitterObj == null)
        {
            GD.Print("[TwitchVoteController] TryConnectSignal: ReplayDispatcher.Emitter is null.");
            return;
        }

        if (!GodotObject.IsInstanceValid((GodotObject)(object)emitterObj))
        {
            GD.Print("[TwitchVoteController] TryConnectSignal: ReplayDispatcher.Emitter is invalid/freed.");
            return;
        }

        ((GodotObject)emitterObj).Connect(
            "InputRequired",
            Callable.From((Action)OnInputRequired),
            0u);

        _signalConnected = true;
        GD.Print($"[TwitchVoteController] Connected to RunReplays InputRequired signal. Emitter={emitterObj.GetType().Name}");
    }

    private static void OnInputRequired()
    {
        try
        {
            var commands = ReplayDispatcher.GetAvailableCommands();
            var commandSummary = commands == null || commands.Count == 0
                ? "none"
                : string.Join(", ", commands.Select(c => c.GetType().Name));
            GD.Print($"[TwitchVoteController] InputRequired fired. availableCommands=[{commandSummary}]");
        }
        catch (Exception ex)
        {
            GD.Print($"[TwitchVoteController] InputRequired fired but GetAvailableCommands threw: {ex.GetType().Name}: {ex.Message} — watchdog will recover.");
            return;
        }

        StartOrRestartVote();
    }

    public static void StartCharacterVote()
    {
        _charVoteController?.StartCharacterVote();
    }

    public static void StartOrRestartVote()
    {
        if (_voteExecutioner == null)
        {
            GD.Print("[TwitchVoteController] StartOrRestartVote: VoteExecutioner is null.");
            return;
        }

        List<ReplayCommand> commands;
        try
        {
            var raw = ReplayDispatcher.GetAvailableCommands();
            if (raw == null || raw.Count == 0)
            {
                GD.Print("[TwitchVoteController] StartOrRestartVote: No available commands — stall risk.");
                return;
            }
            commands = raw;
        }
        catch (Exception ex)
        {
            GD.Print($"[TwitchVoteController] StartOrRestartVote: GetAvailableCommands threw: {ex.GetType().Name}: {ex.Message} — will retry on next watchdog tick.");
            return;
        }

        GD.Print(
            $"[TwitchVoteController] StartOrRestartVote: {commands.Count} raw commands: [{string.Join(", ", commands.Select(c => c.GetType().Name))}] | " +
            $"AwaitingMapMove={_voteExecutioner.AwaitingMapMove} ShopOpened={_voteExecutioner.ShopOpened} " +
            $"AwaitingProceedAfterShop={_voteExecutioner.AwaitingProceedAfterShop} TreasureState={_voteExecutioner.TreasureState}");

        if (_voteExecutioner.AwaitingProceedAfterShop)
        {
            _voteExecutioner.AwaitingProceedAfterShop = false;
            var proceedCmd = commands.FirstOrDefault(c => c is ProceedToMapCommand);
            if (proceedCmd != null)
            {
                GD.Print("[TwitchVoteController] StartOrRestartVote: AwaitingProceedAfterShop — forcing ProceedToMap vote.");
                _voteExecutioner.StartVote(new List<ReplayCommand> { proceedCmd });
                return;
            }
            GD.Print("[TwitchVoteController] StartOrRestartVote: AwaitingProceedAfterShop but no ProceedToMapCommand found — continuing.");
        }

        var hasMap = commands.Any(c => c is MapMoveCommand);
        var hasProceed = commands.Any(c => c is ProceedToMapCommand);
        var hasTakeCard = commands.Any(c => c is TakeCardCommand);

        List<ReplayCommand> filtered;
        if (_voteExecutioner.AwaitingMapMove && hasMap)
        {
            filtered = commands.Where(c => c is MapMoveCommand).ToList();
            GD.Print($"[TwitchVoteController] Filter: AwaitingMapMove — kept {filtered.Count} MapMoveCommands.");
        }
        else if (hasMap && hasProceed)
        {
            filtered = commands.Where(c => c is not MapMoveCommand).ToList();
            GD.Print($"[TwitchVoteController] Filter: hasMap+hasProceed — stripped MapMove, {filtered.Count} remain.");
        }
        else
        {
            filtered = commands.ToList();
        }

        if (hasTakeCard)
        {
            var before = filtered.Count;
            filtered = filtered.Where(c => c is not ClaimRewardCommand).ToList();
            GD.Print($"[TwitchVoteController] Filter: hasTakeCard — stripped ClaimReward, {before}→{filtered.Count}.");
        }

        if (filtered.Any(c => c is ClickGridCardCommand))
        {
            var before = filtered.Count;
            filtered = filtered.Where(c => c is not SelectGridCardCommand and not ChooseRestSiteOptionCommand).ToList();
            GD.Print($"[TwitchVoteController] Filter: ClickGridCard — stripped Select/RestSite, {before}→{filtered.Count}.");
        }

        if (filtered.Any(c => c is CancelGridSelectionCommand) && filtered.Any(c => c is ConfirmGridSelectionCommand))
        {
            var before = filtered.Count;
            filtered = filtered.Where(c => c is CancelGridSelectionCommand or ConfirmGridSelectionCommand).ToList();
            GD.Print($"[TwitchVoteController] Filter: Confirm+Cancel — isolated to 2, {before}→{filtered.Count}.");
        }

        // Shop state machine
        var hasOpenShop = filtered.Any(c => c is OpenShopCommand or OpenFakeShopCommand);
        var hasBuy = filtered.Any(c => c is BuyCardCommand or BuyRelicCommand
            or BuyPotionCommand or BuyCardRemovalCommand);

        if (_voteExecutioner.ShopOpened)
        {
            var before = filtered.Count;
            filtered = filtered.Where(c => c is not OpenShopCommand and not OpenFakeShopCommand and not ProceedToMapCommand).ToList();
            GD.Print($"[TwitchVoteController] Filter: ShopOpened — stripped OpenShop+Proceed, {before}→{filtered.Count}.");
        }
        else if (hasOpenShop && hasBuy)
        {
            var before = filtered.Count;
            filtered = filtered.Where(c => c is not BuyCardCommand and not BuyRelicCommand
                and not BuyPotionCommand and not BuyCardRemovalCommand and not MapMoveCommand).ToList();
            GD.Print($"[TwitchVoteController] Filter: hasOpenShop+hasBuy — stripped buy cmds, {before}→{filtered.Count}.");
        }

        if (!hasOpenShop && !hasBuy)
            _voteExecutioner.ShopOpened = false;

        // Treasure room state machine
        var hasOpenChest = filtered.Any(c => c is OpenChestCommand);
        var hasTakeRelic = filtered.Any(c => c is TakeChestRelicCommand);

        if (_voteExecutioner.TreasureState == VoteExecutioner.ChestState.RelicTaken)
        {
            var before = filtered.Count;
            filtered = filtered.Where(c => c is not OpenChestCommand and not TakeChestRelicCommand).ToList();
            GD.Print($"[TwitchVoteController] Filter: TreasureState=RelicTaken — stripped chest cmds, {before}→{filtered.Count}.");
        }
        else if (_voteExecutioner.TreasureState == VoteExecutioner.ChestState.Opened)
        {
            var before = filtered.Count;
            filtered = filtered.Where(c => c is not OpenChestCommand and not ProceedToMapCommand).ToList();
            GD.Print($"[TwitchVoteController] Filter: TreasureState=Opened — stripped OpenChest+Proceed, {before}→{filtered.Count}.");
        }
        else if (hasOpenChest && hasTakeRelic)
        {
            var before = filtered.Count;
            filtered = filtered.Where(c => c is not TakeChestRelicCommand and not ProceedToMapCommand).ToList();
            GD.Print($"[TwitchVoteController] Filter: hasOpenChest+hasTakeRelic — stripped TakeRelic+Proceed, {before}→{filtered.Count}.");
        }

        if (!hasOpenChest && !hasTakeRelic)
            _voteExecutioner.TreasureState = VoteExecutioner.ChestState.Closed;

        if (!hasMap)
            _voteExecutioner.AwaitingMapMove = false;

        if (filtered.Count == 0)
        {
            GD.Print(
                $"[TwitchVoteController] StartOrRestartVote: filtered to 0 commands — stall. " +
                $"Raw was: [{string.Join(", ", commands.Select(c => c.GetType().Name))}]");
            return;
        }

        CombatOverlay.Refresh();

        var sorted = filtered
            .OrderBy(c => CommandDescriber.GetSortKey(c))
            .ToList();

        GD.Print(
            $"[TwitchVoteController] Starting vote with {sorted.Count} options: [{string.Join(", ", sorted.Select(c => CommandDescriber.Describe(c)))}]");

        _voteExecutioner.StartVote(sorted);
    }
}

[HarmonyPatch(typeof(NGame), "LoadMainMenu")]
public class MainMenuPatch
{
    [HarmonyPostfix]
    public static void Postfix(NGame __instance)
    {
        GD.Print("[TwitchVoteController] LoadMainMenu fired — resetting signal connection.");
        Plugin._signalConnected = false;
        Plugin.SetupVoteExecutioner(__instance);
        Plugin.TryConnectSignal();

        if (Plugin._pendingCharacterVote)
        {
            Plugin._pendingCharacterVote = false;
            GD.Print("[TwitchVoteController] Run ended — starting character vote.");
            Callable.From(Plugin.StartCharacterVote).CallDeferred();
        }
    }
}

[HarmonyPatch(typeof(RunManager), nameof(RunManager.SetUpNewSinglePlayer))]
public class NewRunStartPatch
{
    [HarmonyPrefix]
    public static void Prefix()
    {
        GD.Print("[TwitchVoteController] SetUpNewSinglePlayer — activating replay.");
        RunStartHelper.Activate();
    }

    [HarmonyPostfix]
    public static void Postfix()
    {
        GD.Print("[TwitchVoteController] SetUpNewSinglePlayer complete — connecting signal.");
        Plugin._signalConnected = false;
        Plugin.TryConnectSignal();
    }
}

[HarmonyPatch(typeof(RunManager), nameof(RunManager.SetUpSavedSinglePlayer))]
public class SavedRunStartPatch
{
    [HarmonyPrefix]
    public static void Prefix()
    {
        GD.Print("[TwitchVoteController] SetUpSavedSinglePlayer — activating replay.");
        RunStartHelper.Activate();
    }

    [HarmonyPostfix]
    public static void Postfix()
    {
        GD.Print("[TwitchVoteController] SetUpSavedSinglePlayer complete — connecting signal.");
        Plugin._signalConnected = false;
        Plugin.TryConnectSignal();
    }
}

[HarmonyPatch(typeof(RunManager), nameof(RunManager.OnEnded))]
public class RunEndedPatch
{
    private const double ReturnToMenuDelay = 5.0;

    [HarmonyPostfix]
    public static void Postfix(bool isVictory)
    {
        GD.Print($"[TwitchVoteController] Run ended (isVictory={isVictory}) — returning to main menu in {ReturnToMenuDelay}s.");
        Plugin._pendingCharacterVote = true;

        var game = NGame.Instance;
        if (game == null)
        {
            GD.Print("[TwitchVoteController] RunEndedPatch: NGame.Instance is null, cannot schedule return.");
            return;
        }

        var timer = new Timer();
        timer.OneShot = true;
        timer.WaitTime = ReturnToMenuDelay;
        timer.Timeout += ReturnToMenu;
        game.AddChild(timer);
        timer.Start();
    }

    private static async void ReturnToMenu()
    {
        GD.Print("[TwitchVoteController] Returning to main menu after run end.");
        var game = NGame.Instance;
        if (game != null)
            await game.ReturnToMainMenu();
    }
}
