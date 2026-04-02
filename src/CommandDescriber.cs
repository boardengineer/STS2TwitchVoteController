using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using RunReplays;
using RunReplays.Commands;

namespace STS2Twitch;

public static class CommandDescriber
{
    public static (int TypeOrder, int SubOrder, string Description) GetSortKey(ReplayCommand command)
    {
        if (command is PlayCardCommand cardCmd)
        {
            var handIndex = GetHandIndex(cardCmd.CombatCardIndex);
            var targetIndex = cardCmd.TargetId.HasValue
                ? (int)(CombatOverlay.GetEnemyIndex(cardCmd.TargetId) ?? 999)
                : 0;
            return (0, handIndex * 1000 + targetIndex, Describe(command));
        }

        if (command is TakeCardCommand takeCmd)
        {
            var order = takeCmd.IsSkip ? 998 : takeCmd.IsSacrifice ? 999 : takeCmd.CardIndex;
            return (0, order, Describe(command));
        }

        if (command is ClaimRewardCommand claimCmd)
            return (0, claimCmd.RewardIndex, Describe(command));

        if (command is SelectCardFromScreenCommand selectCmd)
        {
            var order = selectCmd.Index < 0 ? 999 : selectCmd.Index;
            return (0, order, Describe(command));
        }

        if (command is SelectGridCardCommand gridCmd)
            return (0, gridCmd.Indices.Length > 0 ? gridCmd.Indices[0] : 999, Describe(command));

        if (command is SelectHandCardsCommand handCmd)
            return (0, handCmd.HandIndices.Length > 0 ? handCmd.HandIndices[0] : 999, Describe(command));

        if (command is UsePotionCommand or DiscardPotionCommand)
            return (2, 0, Describe(command));

        return (1, 0, Describe(command));
    }

    private static int GetHandIndex(uint combatCardIndex)
    {
        try
        {
            var state = CombatManager.Instance.DebugOnlyGetState();
            if (state == null) return 999;

            var player = state.Players.FirstOrDefault();
            var hand = player?.PlayerCombatState?.Hand.Cards;
            if (hand == null) return 999;

            if (!NetCombatCardDb.Instance.TryGetCard(combatCardIndex, out var card) || card == null)
                return 999;

            for (int i = 0; i < hand.Count; i++)
            {
                if (hand[i] == card)
                    return i;
            }
        }
        catch { }

        return 999;
    }

    public static string Describe(ReplayCommand command)
    {
        if (command is ChooseEventOptionCommand eventCmd)
            return DescribeEventOption(eventCmd);

        if (command is PlayCardCommand cardCmd)
            return DescribePlayCard(cardCmd);

        if (command is TakeCardCommand takeCmd)
            return DescribeTakeCard(takeCmd);

        if (command is SelectCardFromScreenCommand selectCmd)
            return DescribeSelectCard(selectCmd);

        if (command is SelectGridCardCommand gridCmd)
            return DescribeGridCard(gridCmd);

        if (command is SelectHandCardsCommand handCmd)
            return DescribeSelectHandCards(handCmd);

        if (command is ClaimRewardCommand claimCmd)
            return DescribeClaimReward(claimCmd);

        if (command is ChooseRestSiteOptionCommand restCmd)
            return DescribeRestSite(restCmd);

        return command.Describe();
    }

    private static string DescribePlayCard(PlayCardCommand cmd)
    {
        try
        {
            if (!NetCombatCardDb.Instance.TryGetCard(cmd.CombatCardIndex, out var card) || card == null)
                return cmd.Describe();

            var name = card.Title;

            if (cmd.TargetId == null)
                return $"Play {name}";

            var enemyIndex = CombatOverlay.GetEnemyIndex(cmd.TargetId);
            var creature = CombatManager.Instance.DebugOnlyGetState()?.GetCreature(cmd.TargetId);
            if (creature == null)
                return $"Play {name}";

            var targetLabel = enemyIndex != null
                ? $"{creature.Name} #{enemyIndex}"
                : creature.Name;
            return $"Play {name} on {targetLabel}";
        }
        catch (Exception)
        {
            return cmd.Describe();
        }
    }

    private static string DescribeTakeCard(TakeCardCommand cmd)
    {
        if (cmd.IsSkip) return "Skip";
        if (cmd.IsSacrifice) return "Sacrifice";

        try
        {
            var screen = ReplayState.CardRewardSelectionScreen;
            if (screen == null) return cmd.Describe();

            var cardRowField = typeof(NCardRewardSelectionScreen).GetField(
                "_cardRow", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var cardRow = cardRowField?.GetValue(screen) as Godot.Control;
            if (cardRow == null) return cmd.Describe();

            var holders = cardRow.GetChildren()
                .OfType<MegaCrit.Sts2.Core.Nodes.Cards.Holders.NGridCardHolder>().ToList();
            if (cmd.CardIndex < 0 || cmd.CardIndex >= holders.Count)
                return cmd.Describe();

            var card = holders[cmd.CardIndex].CardModel;
            return card != null ? $"Take {card.Title}" : cmd.Describe();
        }
        catch (Exception)
        {
            return cmd.Describe();
        }
    }

    private static readonly FieldInfo? ChooseACardActiveScreenField =
        typeof(ChooseACardScreenCapture).GetField("ActiveScreen", BindingFlags.Static | BindingFlags.NonPublic);

    private static readonly FieldInfo? CardGridActiveScreenField =
        typeof(CardGridScreenCapture).GetField("ActiveScreen", BindingFlags.Static | BindingFlags.NonPublic);

    private static readonly FieldInfo? CardGridCardsField =
        typeof(NCardGridSelectionScreen).GetField("_cards", BindingFlags.Instance | BindingFlags.NonPublic);

    private static string DescribeSelectCard(SelectCardFromScreenCommand cmd)
    {
        if (cmd.Index < 0) return "Skip";

        try
        {
            var screen = ChooseACardActiveScreenField?.GetValue(null) as Node;
            if (screen == null) return cmd.Describe();

            var holder = FindCardHolderByIndex(screen, cmd.Index);
            if (holder == null) return cmd.Describe();

            var cardModel = holder.GetType()
                .GetProperty("CardModel", BindingFlags.Instance | BindingFlags.Public)
                ?.GetValue(holder) as CardModel;

            return cardModel != null ? $"Select {cardModel.Title}" : cmd.Describe();
        }
        catch (Exception)
        {
            return cmd.Describe();
        }
    }

    private static string DescribeGridCard(SelectGridCardCommand cmd)
    {
        try
        {
            var screen = CardGridActiveScreenField?.GetValue(null) as NCardGridSelectionScreen;
            if (screen == null) return cmd.Describe();

            var cards = CardGridCardsField?.GetValue(screen) as IReadOnlyList<CardModel>;
            if (cards == null) return cmd.Describe();

            var names = new List<string>();
            foreach (var idx in cmd.Indices)
            {
                if (idx >= 0 && idx < cards.Count)
                    names.Add(cards[idx].Title);
            }

            return names.Count > 0 ? $"Select {string.Join(", ", names)}" : cmd.Describe();
        }
        catch (Exception)
        {
            return cmd.Describe();
        }
    }

    // Exposed for SelectionOverlay
    internal static Node? GetChooseACardScreen()
        => ChooseACardActiveScreenField?.GetValue(null) as Node;

    internal static NCardGridSelectionScreen? GetCardGridScreen()
        => CardGridActiveScreenField?.GetValue(null) as NCardGridSelectionScreen;

    internal static IReadOnlyList<CardModel>? GetGridCards(NCardGridSelectionScreen screen)
        => CardGridCardsField?.GetValue(screen) as IReadOnlyList<CardModel>;

    internal static Node? FindCardHolderByIndex(Node screen, int index)
    {
        int num = 0;
        foreach (var item in screen.FindChildren("*", "", true, false))
        {
            if (item.GetType().GetProperty("CardModel", BindingFlags.Instance | BindingFlags.Public)?.GetValue(item) is CardModel)
            {
                if (num == index) return item;
                num++;
            }
        }
        return null;
    }

    private static string DescribeSelectHandCards(SelectHandCardsCommand cmd)
    {
        if (cmd.HandIndices.Length == 0)
            return "Select none";

        try
        {
            var state = CombatManager.Instance.DebugOnlyGetState();
            if (state == null) return cmd.Describe();

            var player = state.Players.FirstOrDefault();
            var hand = player?.PlayerCombatState?.Hand.Cards;
            if (hand == null) return cmd.Describe();

            var names = new List<string>();
            foreach (var idx in cmd.HandIndices)
            {
                if (idx >= 0 && idx < hand.Count)
                    names.Add(hand[idx].Title);
            }

            return names.Count > 0 ? $"Select {string.Join(", ", names)}" : cmd.Describe();
        }
        catch (Exception)
        {
            return cmd.Describe();
        }
    }

    private static string DescribeClaimReward(ClaimRewardCommand cmd)
    {
        try
        {
            var screen = ReplayState.ActiveRewardsScreen;
            if (screen == null || !GodotObject.IsInstanceValid(screen))
                return cmd.Describe();

            var buttons = screen.FindChildren("*", "", true, false)
                .Where(n => n.GetType().Name == "NRewardButton")
                .ToList();

            if (cmd.RewardIndex < 0 || cmd.RewardIndex >= buttons.Count)
                return cmd.Describe();

            var rewardProp = buttons[cmd.RewardIndex].GetType()
                .GetProperty("Reward", BindingFlags.Instance | BindingFlags.Public);
            var reward = rewardProp?.GetValue(buttons[cmd.RewardIndex]);
            if (reward == null)
                return cmd.Describe();

            var descProp = reward.GetType().GetProperty("Description", BindingFlags.Instance | BindingFlags.Public);
            var locString = descProp?.GetValue(reward);
            if (locString == null)
                return cmd.Describe();

            var getFormatted = locString.GetType().GetMethod("GetFormattedText");
            var text = getFormatted?.Invoke(locString, null) as string;

            return !string.IsNullOrEmpty(text) ? $"Claim {text}" : cmd.Describe();
        }
        catch (Exception)
        {
            return cmd.Describe();
        }
    }

    private static string DescribeRestSite(ChooseRestSiteOptionCommand cmd)
    {
        try
        {
            var sync = ReplayState.ActiveRestSiteSynchronizer;
            if (sync == null) return cmd.Describe();

            var options = sync.GetLocalOptions();
            var option = options.FirstOrDefault(o => o.OptionId == cmd.OptionId);
            return option != null ? option.Title.GetFormattedText() : cmd.Describe();
        }
        catch (Exception)
        {
            return cmd.Describe();
        }
    }

    private static string DescribeEventOption(ChooseEventOptionCommand cmd)
    {
        if (cmd.RecordedIndex == -1)
            return "Proceed";

        try
        {
            var sync = ReplayState.ActiveEventSynchronizer;
            var evt = sync.Events[0];
            var option = evt.CurrentOptions[cmd.RecordedIndex];
            evt.DynamicVars.AddTo(option.Title);
            var title = option.Title.GetFormattedText();
            return $"{cmd.RecordedIndex + 1}: {title}";
        }
        catch (Exception)
        {
            return cmd.Describe();
        }
    }
}
