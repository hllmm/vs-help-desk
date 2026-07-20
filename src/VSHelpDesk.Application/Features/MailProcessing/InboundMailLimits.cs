namespace VSHelpDesk.Application.Features.MailProcessing;

/// <summary>Shared caps for untrusted inbound email content (Week 2 hardening).</summary>
public static class InboundMailLimits
{
    /// <summary>Maximum stored body length (characters) for inbound customer mail.</summary>
    public const int MaxBodyLength = 256 * 1024;

    public const string EmptyBodyPlaceholder = "(empty body)";

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
}
