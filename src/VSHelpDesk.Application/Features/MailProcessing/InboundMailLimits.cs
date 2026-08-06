namespace VSHelpDesk.Application.Features.MailProcessing;

/// <summary>Shared caps for untrusted inbound email content (Week 2 hardening).</summary>
public static class InboundMailLimits
{
    public const int MaxMessageIdLength = 998;
    public const int MaxSubjectLength = 500;
    public const int MaxDisplayNameLength = 200;
    public const int MaxAddressLength = 255;
    public const int MaxBodyLength = 256 * 1024;
    public const int MaxProcessingNoteLength = 500;

    public const string EmptySubjectPlaceholder = "Konusuz e-posta";
    public const string EmptyBodyPlaceholder = "İleti içeriği bulunamadı.";

    public static string NormalizeBody(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return EmptyBodyPlaceholder;
        }

        var trimmed = body.Trim();
        if (trimmed.Length <= MaxBodyLength)
        {
            return trimmed;
        }

        return trimmed[..MaxBodyLength];
    }

    public static string NormalizeSubject(string? subject)
    {
        if (string.IsNullOrWhiteSpace(subject))
        {
            return EmptySubjectPlaceholder;
        }

        var trimmed = subject.Trim();
        if (trimmed.Length <= MaxSubjectLength)
        {
            return trimmed;
        }

        return trimmed[..MaxSubjectLength];
    }

    public static string NormalizeDisplayName(string? displayName, string addressFallback)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return Truncate(addressFallback, MaxDisplayNameLength);
        }

        return Truncate(displayName.Trim(), MaxDisplayNameLength);
    }

    public static string? BoundProcessingNote(string? note)
    {
        if (string.IsNullOrWhiteSpace(note))
        {
            return null;
        }

        var trimmed = note.Trim();
        return Truncate(trimmed, MaxProcessingNoteLength);
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];
}
