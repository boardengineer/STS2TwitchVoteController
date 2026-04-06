using System.Collections.Generic;
using System.Linq;
using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;

namespace STS2Twitch.Overlays;

public static class CombatOverlay
{
    private static readonly List<Label> _labels = new();

    public static void Refresh()
    {
        ClearLabels();

        var room = NCombatRoom.Instance;
        if (room == null)
            return;

        var enemies = room.CreatureNodes
            .Where(n => GodotObject.IsInstanceValid(n)
                        && n.Entity.IsEnemy
                        && n.Entity.IsAlive
                        && n.Entity.CombatId.HasValue)
            .OrderBy(n => n.Entity.CombatId!.Value)
            .ToList();

        for (int i = 0; i < enemies.Count; i++)
        {
            var node = enemies[i];
            var label = new Label();
            label.Text = (i + 1).ToString();
            label.AddThemeColorOverride("font_color", Colors.Yellow);
            label.AddThemeFontSizeOverride("font_size", 48);
            label.AddThemeColorOverride("font_outline_color", Colors.Black);
            label.AddThemeConstantOverride("outline_size", 8);
            label.HorizontalAlignment = HorizontalAlignment.Center;
            label.VerticalAlignment = VerticalAlignment.Center;
            label.Size = new Vector2(80, 80);
            label.Position = new Vector2(-40, -120);
            label.ZIndex = 100;
            node.AddChild(label);
            _labels.Add(label);
        }
    }

    public static void ClearLabels()
    {
        foreach (var label in _labels)
        {
            if (GodotObject.IsInstanceValid(label))
                label.QueueFree();
        }
        _labels.Clear();
    }

    public static int? GetEnemyIndex(uint? combatId)
    {
        if (combatId == null)
            return null;

        var state = CombatManager.Instance.DebugOnlyGetState();
        if (state == null)
            return null;

        var enemies = state.Enemies
            .Where(e => e.IsAlive && e.CombatId.HasValue)
            .OrderBy(e => e.CombatId!.Value)
            .ToList();

        for (int i = 0; i < enemies.Count; i++)
        {
            if (enemies[i].CombatId == combatId)
                return i + 1;
        }

        return null;
    }
}
