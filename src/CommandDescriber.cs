using System;
using MegaCrit.Sts2.Core.Events;
using RunReplays;
using RunReplays.Commands;

namespace STS2Twitch;

public static class CommandDescriber
{
    public static string Describe(ReplayCommand command)
    {
        if (command is ChooseEventOptionCommand eventCmd)
            return DescribeEventOption(eventCmd);

        return command.Describe();
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
