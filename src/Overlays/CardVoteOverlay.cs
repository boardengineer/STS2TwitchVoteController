using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Potions;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using RunReplays.Commands;

namespace STS2Twitch.Overlays;

public static class CardVoteOverlay
{
    private static readonly List<Label> _labels = new();

    public static void Refresh(List<ReplayCommand> options, Dictionary<string, int> votes)
    {
        ClearLabels();

        // Build tally: option index (1-based) -> vote count
        var tally = new Dictionary<int, int>();
        foreach (var choice in votes.Values)
        {
            tally.TryGetValue(choice, out var count);
            tally[choice] = count + 1;
        }

        // Potion overlays (work both in and out of combat)
        RefreshPotionOverlays(options, tally);

        var hand = NPlayerHand.Instance;
        if (hand == null)
            return;

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

        // End turn button overlay
        var endTurnButton = NCombatRoom.Instance?.Ui?.EndTurnButton;
        if (endTurnButton != null && GodotObject.IsInstanceValid(endTurnButton))
        {
            for (int i = 0; i < options.Count; i++)
            {
                if (options[i] is not EndTurnCommand)
                    continue;

                tally.TryGetValue(i + 1, out var etVotes);
                var etLabel = new Label();
                etLabel.Text = etVotes > 0 ? $"[{i + 1}]:{etVotes}" : $"[{i + 1}]";
                etLabel.AddThemeColorOverride("font_color", Colors.Yellow);
                etLabel.AddThemeFontSizeOverride("font_size", 28);
                etLabel.AddThemeColorOverride("font_outline_color", Colors.Black);
                etLabel.AddThemeConstantOverride("outline_size", 6);
                etLabel.HorizontalAlignment = HorizontalAlignment.Center;
                etLabel.Size = new Vector2(80, 40);
                etLabel.Position = new Vector2(endTurnButton.Size.X / 2 - 40, -10);
                etLabel.ZIndex = 100;
                endTurnButton.AddChild(etLabel);
                _labels.Add(etLabel);
                break;
            }
        }
    }

    private static readonly FieldInfo? PotionHoldersField =
        typeof(NPotionContainer).GetField("_holders", BindingFlags.Instance | BindingFlags.NonPublic);

    private static void RefreshPotionOverlays(List<ReplayCommand> options, Dictionary<int, int> tally)
    {
        var potionContainer = NRun.Instance?.GlobalUi?.TopBar?.PotionContainer;
        if (potionContainer == null) return;

        var holders = PotionHoldersField?.GetValue(potionContainer) as List<NPotionHolder>;
        if (holders == null) return;

        // Group options by potion slot
        var slotOptions = new Dictionary<int, List<(int optionIndex, int voteCount, bool isDiscard)>>();

        for (int i = 0; i < options.Count; i++)
        {
            int slotIndex;
            bool isDiscard;
            if (options[i] is UsePotionCommand useCmd)
            {
                slotIndex = (int)useCmd.PotionIndex;
                isDiscard = false;
            }
            else if (options[i] is DiscardPotionCommand discardCmd)
            {
                slotIndex = discardCmd.SlotIndex;
                isDiscard = true;
            }
            else
                continue;

            if (!slotOptions.ContainsKey(slotIndex))
                slotOptions[slotIndex] = new();

            tally.TryGetValue(i + 1, out var voteCount);
            slotOptions[slotIndex].Add((i + 1, voteCount, isDiscard));
        }

        foreach (var (slotIndex, opts) in slotOptions)
        {
            if (slotIndex < 0 || slotIndex >= holders.Count)
                continue;

            var holder = holders[slotIndex];
            if (!GodotObject.IsInstanceValid(holder))
                continue;

            for (int j = 0; j < opts.Count; j++)
            {
                var o = opts[j];
                var label = new Label();
                label.Text = o.voteCount > 0 ? $"[{o.optionIndex}]:{o.voteCount}" : $"[{o.optionIndex}]";
                label.AddThemeColorOverride("font_color", o.isDiscard ? Colors.Red : Colors.Yellow);
                label.AddThemeFontSizeOverride("font_size", 24);
                label.AddThemeColorOverride("font_outline_color", Colors.Black);
                label.AddThemeConstantOverride("outline_size", 6);
                label.HorizontalAlignment = HorizontalAlignment.Center;
                label.Size = new Vector2(100, 30);
                label.Position = new Vector2(holder.Size.X / 2 - 50, holder.Size.Y + 5 + 30 * j);
                label.ZIndex = 100;
                holder.AddChild(label);
                _labels.Add(label);
            }
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
