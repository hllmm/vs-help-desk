using System.Text.RegularExpressions;

namespace VSHelpDesk.Domain.Tickets;

/// <summary>BR-005 — extract canonical ticket numbers from free text (e.g. email subject).</summary>
public static partial class TicketNumberParser
{
    /// <summary>
    /// Alphanumeric-boundary aware: avoids matching inside tokens like <c>CVS-000001</c>
    /// or with suffixes like <c>VS-1abc</c>. Caps at six digits so <c>VS-1234567</c>
    /// does not silently truncate. Scans past invalid candidates (e.g. <c>VS-0</c>).
    /// </summary>
    [GeneratedRegex(
        @"(?<![A-Za-z0-9])VS-(\d{1,6})(?![A-Za-z0-9])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EmbeddedPattern();

    /// <summary>
    /// Finds the first valid VS-###### style token and normalizes to six-digit canonical form.
    /// </summary>
    public static bool TryFindInText(string? text, out string ticketNumber)
    {
        ticketNumber = string.Empty;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        foreach (Match match in EmbeddedPattern().Matches(text))
        {
            if (long.TryParse(match.Groups[1].Value, out var sequence) &&
                sequence is > 0 and <= TicketNumberFormat.MaxSequenceValue)
            {
                ticketNumber = TicketNumberFormat.Format(sequence);
                return true;
            }
        }

        return false;
    }
}
