using System;
using System.Reflection;
using Godot;
using HarmonyLib;
using RunReplays;

namespace STS2Twitch;

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
