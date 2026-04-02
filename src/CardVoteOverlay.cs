using System.Collections.Generic;
using System.Linq;
using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Combat;
using RunReplays.Commands;

namespace STS2Twitch;

public static class CardVoteOverlay
{
    private static readonly List<Label> _labels = new();

    public static void Refresh(List<ReplayCommand> options, Dictionary<string, int> votes)
    {
        ClearLabels();

        var hand = NPlayerHand.Instance;
        if (hand == null)
            return;

        // Build tally: option index (1-based) -> vote count
        var tally = new Dictionary<int, int>();
        foreach (var choice in votes.Values)
        {
            tally.TryGetValue(choice, out var count);
            tally[choice] = count + 1;
        }

        // Map each card in hand to its vote options
        // A card may have multiple options (different targets), so group by card
        var cardOptions = new Dictionary<CardModel, List<(int optionIndex, int voteCount)>>();

        for (int i = 0; i < options.Count; i++)
        {
            if (options[i] is not PlayCardCommand cardCmd)
                continue;

            if (!NetCombatCardDb.Instance.TryGetCard(cardCmd.CombatCardIndex, out var card) || card == null)
                continue;

            if (!cardOptions.ContainsKey(card))
                cardOptions[card] = new();

            tally.TryGetValue(i + 1, out var voteCount);
            cardOptions[card].Add((i + 1, voteCount));
        }

        // Also handle SelectHandCardsCommand — map hand indices to cards
        var handCards = CombatManager.Instance.DebugOnlyGetState()
            ?.Players.FirstOrDefault()?.PlayerCombatState?.Hand.Cards;

        if (handCards != null)
        {
            for (int i = 0; i < options.Count; i++)
            {
                if (options[i] is not SelectHandCardsCommand handCmd)
                    continue;

                foreach (var idx in handCmd.HandIndices)
                {
                    if (idx >= 0 && idx < handCards.Count)
                    {
                        var card = handCards[idx];
                        if (!cardOptions.ContainsKey(card))
                            cardOptions[card] = new();

                        tally.TryGetValue(i + 1, out var vc);
                        cardOptions[card].Add((i + 1, vc));
                    }
                }
            }
        }

        foreach (var holder in hand.ActiveHolders)
        {
            var model = holder.CardNode?.Model;
            if (model == null || !cardOptions.TryGetValue(model, out var opts))
                continue;

            var text = string.Join(" ", opts.Select(o =>
                o.voteCount > 0 ? $"[{o.optionIndex}]:{o.voteCount}" : $"[{o.optionIndex}]"));

            var label = new Label();
            label.Text = text;
            label.AddThemeColorOverride("font_color", Colors.Yellow);
            label.AddThemeFontSizeOverride("font_size", 28);
            label.AddThemeColorOverride("font_outline_color", Colors.Black);
            label.AddThemeConstantOverride("outline_size", 6);
            label.HorizontalAlignment = HorizontalAlignment.Center;
            label.Size = new Vector2(200, 40);
            label.Position = new Vector2(-100 + holder.Size.X / 2, -45);
            label.ZIndex = 100;
            holder.AddChild(label);
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
