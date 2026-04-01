using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Godot;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using RunReplays;
using RunReplays.Commands;

namespace STS2Twitch;

public static class SelectionOverlay
{
    private static readonly List<Label> _labels = new();

    private static readonly FieldInfo? CardRowField =
        typeof(NCardRewardSelectionScreen).GetField("_cardRow", BindingFlags.Instance | BindingFlags.NonPublic);

    private static readonly FieldInfo? ChoicesContainerField =
        typeof(NRestSiteRoom).GetField("_choicesContainer", BindingFlags.Instance | BindingFlags.NonPublic);

    public static void Refresh(List<ReplayCommand> options, Dictionary<string, int> votes)
    {
        ClearLabels();

        var tally = new Dictionary<int, int>();
        foreach (var choice in votes.Values)
        {
            tally.TryGetValue(choice, out var count);
            tally[choice] = count + 1;
        }

        TryOverlayCardRewards(options, tally);
        TryOverlayRewardButtons(options, tally);
        TryOverlayRestSite(options, tally);
        TryOverlayChooseACard(options, tally);
        TryOverlayGridCards(options, tally);
    }

    private static void TryOverlayCardRewards(List<ReplayCommand> options, Dictionary<int, int> tally)
    {
        var screen = ReplayState.CardRewardSelectionScreen;
        if (screen == null)
            return;

        var cardRow = CardRowField?.GetValue(screen) as Control;
        if (cardRow == null)
            return;

        var holders = cardRow.GetChildren().OfType<NGridCardHolder>().ToList();

        for (int i = 0; i < options.Count; i++)
        {
            if (options[i] is not TakeCardCommand takeCmd)
                continue;
            if (takeCmd.IsSkip || takeCmd.IsSacrifice)
                continue;
            if (takeCmd.CardIndex < 0 || takeCmd.CardIndex >= holders.Count)
                continue;

            var holder = holders[takeCmd.CardIndex];
            tally.TryGetValue(i + 1, out var voteCount);
            AddLabel(holder, i + 1, voteCount, new Vector2(-40, -45));
        }
    }

    private static void TryOverlayRewardButtons(List<ReplayCommand> options, Dictionary<int, int> tally)
    {
        var screen = ReplayState.ActiveRewardsScreen;
        if (screen == null || !screen.IsInsideTree())
            return;

        // Find reward buttons the same way RunReplays does internally
        var buttons = screen.FindChildren("*", "", true, false)
            .Where(n => n.GetType().Name == "NRewardButton")
            .OfType<Control>()
            .ToList();

        for (int i = 0; i < options.Count; i++)
        {
            if (options[i] is not ClaimRewardCommand claimCmd)
                continue;
            if (claimCmd.RewardIndex < 0 || claimCmd.RewardIndex >= buttons.Count)
                continue;

            tally.TryGetValue(i + 1, out var voteCount);
            AddLabel(buttons[claimCmd.RewardIndex], i + 1, voteCount, new Vector2(-40, -10));
        }
    }

    private static void TryOverlayRestSite(List<ReplayCommand> options, Dictionary<int, int> tally)
    {
        var room = NRestSiteRoom.Instance;
        if (room == null)
            return;

        var container = ChoicesContainerField?.GetValue(room) as Control;
        if (container == null)
            return;

        var buttons = container.GetChildren().OfType<Control>().ToList();

        for (int i = 0; i < options.Count; i++)
        {
            if (options[i] is not ChooseRestSiteOptionCommand)
                continue;

            // Rest site options are ordered same as container children
            var restIndex = 0;
            for (int j = 0; j < i; j++)
            {
                if (options[j] is ChooseRestSiteOptionCommand)
                    restIndex++;
            }

            if (restIndex >= buttons.Count)
                continue;

            tally.TryGetValue(i + 1, out var voteCount);
            AddLabel(buttons[restIndex], i + 1, voteCount, new Vector2(-40, -10));
        }
    }

    private static void TryOverlayChooseACard(List<ReplayCommand> options, Dictionary<int, int> tally)
    {
        var screen = CommandDescriber.GetChooseACardScreen();
        if (screen == null)
            return;

        for (int i = 0; i < options.Count; i++)
        {
            if (options[i] is not SelectCardFromScreenCommand selectCmd)
                continue;
            if (selectCmd.Index < 0)
                continue;

            var holder = CommandDescriber.FindCardHolderByIndex(screen, selectCmd.Index);
            if (holder is not Control control)
                continue;

            tally.TryGetValue(i + 1, out var voteCount);
            AddLabel(control, i + 1, voteCount, new Vector2(-40, -45));
        }
    }

    private static void TryOverlayGridCards(List<ReplayCommand> options, Dictionary<int, int> tally)
    {
        var screen = CommandDescriber.GetCardGridScreen();
        if (screen == null)
            return;

        for (int i = 0; i < options.Count; i++)
        {
            if (options[i] is not SelectGridCardCommand gridCmd)
                continue;
            if (gridCmd.Indices.Length == 0)
                continue;

            var holder = CommandDescriber.FindCardHolderByIndex(screen, gridCmd.Indices[0]);
            if (holder is not Control control)
                continue;

            tally.TryGetValue(i + 1, out var voteCount);
            AddLabel(control, i + 1, voteCount, new Vector2(-40, -45));
        }
    }

    private static void AddLabel(Control parent, int optionNumber, int voteCount, Vector2 offset)
    {
        var label = new Label();
        label.Text = voteCount > 0 ? $"[{optionNumber}]:{voteCount}" : $"[{optionNumber}]";
        label.AddThemeColorOverride("font_color", Colors.Yellow);
        label.AddThemeFontSizeOverride("font_size", 32);
        label.AddThemeColorOverride("font_outline_color", Colors.Black);
        label.AddThemeConstantOverride("outline_size", 6);
        label.HorizontalAlignment = HorizontalAlignment.Center;
        label.Size = new Vector2(80, 40);
        label.Position = offset;
        label.ZIndex = 100;
        parent.AddChild(label);
        _labels.Add(label);
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
