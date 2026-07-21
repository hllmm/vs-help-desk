using System.Globalization;
using System.Text.RegularExpressions;

namespace VSHelpDesk.Domain.Tickets;

/// <summary>BR-003 — human-facing ticket numbers such as VS-000001.</summary>
public static partial class TicketNumberFormat
{
    public const string Prefix = "VS-";
    public const int SequenceWidth = 6;
    public const long MaxSequenceValue = 999_999;

    [GeneratedRegex(@"^VS-(\d{6})$", RegexOptions.CultureInvariant)]
    private static partial Regex CanonicalPattern();

    public static string Format(long sequenceValue)
    {
        if (sequenceValue is <= 0 or > MaxSequenceValue)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sequenceValue),
                $"Sequence value must be between 1 and {MaxSequenceValue}.");
        }

        return Prefix + sequenceValue.ToString(
            $"D{SequenceWidth}",
            CultureInfo.InvariantCulture);
    }

    public static bool IsCanonical(string ticketNumber) =>
        !string.IsNullOrWhiteSpace(ticketNumber) && CanonicalPattern().IsMatch(ticketNumber);
}
