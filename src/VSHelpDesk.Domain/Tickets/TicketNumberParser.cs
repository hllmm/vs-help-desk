using System.Text.RegularExpressions;

namespace VSHelpDesk.Domain.Tickets;

/// <summary>BR-005 — extract canonical ticket numbers from free text (e.g. email subject).</summary>
public static partial class TicketNumberParser
{
    [GeneratedRegex(@"VS-(\d{1,6})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EmbeddedPattern();

    /// <summary>
    /// Finds the first VS-###### style token and normalizes to six-digit canonical form when possible.
    /// </summary>
    public static bool TryFindInText(string? text, out string ticketNumber)
    {
        ticketNumber = string.Empty;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var match = EmbeddedPattern().Match(text);
        if (!match.Success || !long.TryParse(match.Groups[1].Value, out var sequence) || sequence <= 0)
        {
            return false;
        }

        ticketNumber = TicketNumberFormat.Format(sequence);
        return true;
    }
}
