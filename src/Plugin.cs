using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Runs;
using RunReplays;

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

        if (_voteExecutioner == null)
            return;

        var commands = ReplayDispatcher.GetAvailableCommands();
        if (commands == null || commands.Count == 0)
            return;

        CombatOverlay.Refresh();

        var sorted = commands
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

    public static void Activate()
    {
        ReplayActiveField?.SetValue(null, true);
        ReplayDispatcher.GameSpeed = 1.0f;
        GD.Print("[TwitchVoteController] ReplayActive set before run start.");
    }
}
