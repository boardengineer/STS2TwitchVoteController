using System;
using System.Linq;
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
