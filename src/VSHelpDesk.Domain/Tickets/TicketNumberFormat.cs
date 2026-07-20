using System.Globalization;
using System.Text.RegularExpressions;

namespace VSHelpDesk.Domain.Tickets;

/// <summary>BR-003 — human-facing ticket numbers such as VS-000001.</summary>
public static partial class TicketNumberFormat
{
    public const string Prefix = "VS-";
    public const int SequenceWidth = 6;

    [GeneratedRegex(@"^VS-(\d{6})$", RegexOptions.CultureInvariant)]
    private static partial Regex CanonicalPattern();

    public static string Format(long sequenceValue)
    {
        if (sequenceValue <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sequenceValue), "Sequence value must be positive.");
        }

        return string.Create(
            Prefix.Length + SequenceWidth,
            sequenceValue,
            static (span, value) =>
            {
                Prefix.AsSpan().CopyTo(span);
                value.TryFormat(
                    span[Prefix.Length..],
                    out _,
                    "D6",
                    CultureInfo.InvariantCulture);
            });
    }

    public static bool IsCanonical(string ticketNumber) =>
        !string.IsNullOrWhiteSpace(ticketNumber) && CanonicalPattern().IsMatch(ticketNumber);
}
