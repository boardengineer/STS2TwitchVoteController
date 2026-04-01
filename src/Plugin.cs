using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Debug;
using RunReplays;
using RunReplays.Commands;

namespace STS2Twitch;

[ModInitializer("Initialize")]
public class Plugin
{
    private static TwitchIrcClient? _ircClient;
    private static Timer? _flushTimer;
    private static bool _signalConnected;
    private static List<ReplayCommand>? _availableCommands;

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
        _ircClient.OnMessageReceived += (username, message) =>
        {
            DevConsoleLogger.Enqueue($"[Chat] {username}: {message}");
        };
        _ircClient.Start();
        GD.Print("[TwitchVoteController] IRC client started.");
    }

    public static void SetupFlushTimer(Node parent)
    {
        if (_flushTimer != null)
            return;

        _flushTimer = new Timer();
        _flushTimer.WaitTime = 0.1;
        _flushTimer.Autostart = true;
        _flushTimer.Timeout += DevConsoleLogger.FlushToConsole;
        parent.AddChild(_flushTimer);
        GD.Print("[TwitchVoteController] Flush timer created.");
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
        if (_ircClient == null)
            return;

        var commands = ReplayDispatcher.GetAvailableCommands();
        if (commands == null || commands.Count == 0)
            return;

        var descriptions = commands.Select(c => CommandDescriber.Describe(c)).OrderBy(d => d).ToList();
        var lastDescriptions = _availableCommands?.Select(c => CommandDescriber.Describe(c)).OrderBy(d => d).ToList();

        if (lastDescriptions != null && descriptions.SequenceEqual(lastDescriptions))
            return;

        _availableCommands = commands;

        var message = "Available commands: " + string.Join(", ", descriptions);
        _ircClient.SendMessage(message);
        DevConsoleLogger.Enqueue($"[TwitchVoteController] Sent to chat: {message}");
    }
}

[HarmonyPatch(typeof(NGame), "LoadMainMenu")]
public class MainMenuPatch
{
    [HarmonyPostfix]
    public static void Postfix(NGame __instance)
    {
        DevConsoleLogger.FlushToConsole();
        Plugin.SetupFlushTimer(__instance);
        Plugin.TryConnectSignal();
    }
}
