using System.Collections.Generic;
using System.Linq;
using Godot;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using RunReplays.Commands;

namespace STS2Twitch;

public static class EventOverlay
{
    private static readonly List<Label> _labels = new();

    public static void Refresh(List<ReplayCommand> options, Dictionary<string, int> votes)
    {
        ClearLabels();

        var room = NEventRoom.Instance;
        var layout = room?.Layout;
        if (layout == null)
            return;

        var buttons = layout.OptionButtons.ToList();

        var tally = new Dictionary<int, int>();
        foreach (var choice in votes.Values)
        {
            tally.TryGetValue(choice, out var count);
            tally[choice] = count + 1;
        }

        // Match event commands to buttons by their RecordedIndex
        for (int i = 0; i < options.Count; i++)
        {
            if (options[i] is not ChooseEventOptionCommand eventCmd)
                continue;
            if (eventCmd.RecordedIndex < 0 || eventCmd.RecordedIndex >= buttons.Count)
                continue;

            var button = buttons[eventCmd.RecordedIndex];
            tally.TryGetValue(i + 1, out var voteCount);

            var label = new Label();
            label.Text = voteCount > 0 ? $"[{i + 1}]:{voteCount}" : $"[{i + 1}]";
            label.AddThemeColorOverride("font_color", Colors.Yellow);
            label.AddThemeFontSizeOverride("font_size", 32);
            label.AddThemeColorOverride("font_outline_color", Colors.Black);
            label.AddThemeConstantOverride("outline_size", 6);
            label.HorizontalAlignment = HorizontalAlignment.Center;
            label.Size = new Vector2(80, 40);
            label.Position = new Vector2(-40, -10);
            label.ZIndex = 100;
            button.AddChild(label);
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
}
