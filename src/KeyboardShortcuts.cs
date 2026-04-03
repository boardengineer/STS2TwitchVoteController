using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes;
using RunReplays;

namespace STS2Twitch;

[HarmonyPatch(typeof(NGame), "_Input")]
public static class KeyboardShortcuts
{
    [HarmonyPrefix]
    public static void Prefix(InputEvent inputEvent)
    {
        if (inputEvent is not InputEventKey keyEvent || !keyEvent.Pressed)
            return;
        
        if (keyEvent.Keycode == Key.F5)
        {
            PlayerActionBuffer.LogMigrationWarning("[KeyboardShortcuts] F5 pressed, starting/restarting vote");
            Plugin.StartOrRestartVote();
        }
    }
}
