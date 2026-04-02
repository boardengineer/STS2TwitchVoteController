using System;
using System.Linq;
using MegaCrit.Sts2.Core.Models;

namespace STS2Twitch;

public static class ChatCommands
{
    public static string? TryHandle(string message)
    {
        var trimmed = message.Trim();
        if (!trimmed.StartsWith("!"))
            return null;

        var spaceIndex = trimmed.IndexOf(' ');
        if (spaceIndex < 0)
            return null;

        var command = trimmed[..spaceIndex].ToLowerInvariant();
        var query = trimmed[(spaceIndex + 1)..].Trim();

        if (string.IsNullOrEmpty(query))
            return null;

        return command switch
        {
            "!card" => LookupCard(query),
            "!relic" => LookupRelic(query),
            "!potion" => LookupPotion(query),
            _ => null
        };
    }

    private static string LookupCard(string query)
    {
        var match = ModelDb.AllCards
            .FirstOrDefault(c => c.Title.Equals(query, StringComparison.OrdinalIgnoreCase))
            ?? ModelDb.AllCards
                .FirstOrDefault(c => c.Title.Contains(query, StringComparison.OrdinalIgnoreCase));

        if (match == null)
            return $"Card '{query}' not found.";

        var desc = match.Description.GetFormattedText();
        return $"{match.Title}: {StripBBCode(desc)}";
    }

    private static string LookupRelic(string query)
    {
        var match = ModelDb.AllRelics
            .FirstOrDefault(r => r.Title.GetFormattedText().Equals(query, StringComparison.OrdinalIgnoreCase))
            ?? ModelDb.AllRelics
                .FirstOrDefault(r => r.Title.GetFormattedText().Contains(query, StringComparison.OrdinalIgnoreCase));

        if (match == null)
            return $"Relic '{query}' not found.";

        var title = match.Title.GetFormattedText();
        var desc = match.Description.GetFormattedText();
        return $"{StripBBCode(title)}: {StripBBCode(desc)}";
    }

    private static string LookupPotion(string query)
    {
        var match = ModelDb.AllPotions
            .FirstOrDefault(p => p.Title.GetFormattedText().Equals(query, StringComparison.OrdinalIgnoreCase))
            ?? ModelDb.AllPotions
                .FirstOrDefault(p => p.Title.GetFormattedText().Contains(query, StringComparison.OrdinalIgnoreCase));

        if (match == null)
            return $"Potion '{query}' not found.";

        var title = match.Title.GetFormattedText();
        var desc = match.Description.GetFormattedText();
        return $"{StripBBCode(title)}: {StripBBCode(desc)}";
    }

    private static string StripBBCode(string text)
    {
        // Remove BBCode tags like [color=#fff]...[/color], [b]...[/b], etc.
        var result = System.Text.RegularExpressions.Regex.Replace(text, @"\[/?[^\]]+\]", "");
        return result.Trim();
    }
}
