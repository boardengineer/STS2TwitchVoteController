using System;
using System.IO;
using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Debug;

namespace STS2Twitch;

[ModInitializer("Initialize")]
public class Plugin
{
    private static TwitchIrcClient? _ircClient;
    private static Timer? _flushTimer;

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
}

[HarmonyPatch(typeof(NGame), "LoadMainMenu")]
public class MainMenuPatch
{
    [HarmonyPostfix]
    public static void Postfix(NGame __instance)
    {
        DevConsoleLogger.FlushToConsole();
        Plugin.SetupFlushTimer(__instance);
    }
}
