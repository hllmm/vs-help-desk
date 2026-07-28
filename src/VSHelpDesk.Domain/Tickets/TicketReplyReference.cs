using System.Text.RegularExpressions;

namespace VSHelpDesk.Domain.Tickets;

public static partial class TicketReplyReference
{
    [GeneratedRegex(
        @"(?<![A-Za-z0-9])\[(VS-\d{6}):R-([A-Fa-f0-9]{32})\](?![A-Za-z0-9])",
        RegexOptions.CultureInvariant)]
    private static partial Regex EmbeddedPattern();

    [GeneratedRegex(@"^[a-f0-9]{32}$", RegexOptions.CultureInvariant)]
    private static partial Regex TokenPattern();

    public static string Format(string ticketNumber, string replyToken)
    {
        if (!TicketNumberParser.TryFindInText(ticketNumber, out var canonical)
            || !string.Equals(
                canonical,
                ticketNumber,
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Canonical ticket number is required.",
                nameof(ticketNumber));
        }

        if (string.IsNullOrWhiteSpace(replyToken)
            || !TokenPattern().IsMatch(replyToken))
        {
            throw new ArgumentException(
                "A lowercase 32-character reply token is required.",
                nameof(replyToken));
        }

        return $"[{canonical}:R-{replyToken}]";
    }

    public static bool TryFindInText(
        string? text,
        out string ticketNumber,
        out string replyToken)
    {
        ticketNumber = string.Empty;
        replyToken = string.Empty;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var match = EmbeddedPattern().Match(text);
        if (!match.Success
            || !TicketNumberParser.TryFindInText(
                match.Groups[1].Value,
                out ticketNumber))
        {
            return false;
        }

        replyToken = match.Groups[2].Value.ToLowerInvariant();
        return true;
    }
}
