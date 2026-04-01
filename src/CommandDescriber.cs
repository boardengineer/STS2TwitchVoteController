using System;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using RunReplays;
using RunReplays.Commands;

namespace STS2Twitch;

public static class CommandDescriber
{
    public static string Describe(ReplayCommand command)
    {
        if (command is ChooseEventOptionCommand eventCmd)
            return DescribeEventOption(eventCmd);

        if (command is PlayCardCommand cardCmd)
            return DescribePlayCard(cardCmd);

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
