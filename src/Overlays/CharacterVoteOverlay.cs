using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Godot;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;

namespace STS2Twitch.Overlays;

public static class CharacterVoteOverlay
{
    private static readonly List<Label> _labels = new();

    private static readonly FieldInfo? CharButtonContainerField =
        typeof(NCharacterSelectScreen).GetField("_charButtonContainer", BindingFlags.Instance | BindingFlags.NonPublic);

    public static void Refresh(NCharacterSelectScreen screen, List<CharacterModel> characters, Dictionary<string, int> votes)
    {
        ClearLabels();

        var tally = new Dictionary<int, int>();
        foreach (var choice in votes.Values)
        {
            tally.TryGetValue(choice, out var count);
            tally[choice] = count + 1;
        }

        var container = CharButtonContainerField?.GetValue(screen) as Control;
        if (container == null)
            return;

        var buttons = container.GetChildren().OfType<NCharacterSelectButton>().ToList();

        for (int i = 0; i < characters.Count; i++)
        {
            var character = characters[i];
            var button = buttons.FirstOrDefault(b => b.Character == character);
            if (button == null || !GodotObject.IsInstanceValid(button))
                continue;

            tally.TryGetValue(i + 1, out var voteCount);
            AddLabel(button, i + 1, voteCount);
        }
    }

    private static void AddLabel(Control parent, int optionNumber, int voteCount)
    {
        var label = new Label();
        label.Text = voteCount > 0 ? $"[{optionNumber}]: {voteCount}" : $"[{optionNumber}]";
        label.AddThemeColorOverride("font_color", Colors.Yellow);
        label.AddThemeFontSizeOverride("font_size", 32);
        label.AddThemeColorOverride("font_outline_color", Colors.Black);
        label.AddThemeConstantOverride("outline_size", 6);
        label.HorizontalAlignment = HorizontalAlignment.Center;
        label.Size = new Vector2(120, 48);
        label.Position = new Vector2(parent.Size.X / 2f - 60f, parent.Size.Y - 60f);
        label.ZIndex = 100;
        parent.AddChild(label);
        _labels.Add(label);
    }

    public static NCharacterSelectButton? FindButton(NCharacterSelectScreen screen, CharacterModel character)
    {
        var container = CharButtonContainerField?.GetValue(screen) as Control;
        return container?.GetChildren()
            .OfType<NCharacterSelectButton>()
            .FirstOrDefault(b => b.Character == character);
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
