namespace VSHelpDesk.Infrastructure.Email;

/// <summary>
/// Trusted MTA authentication verdict parsed from Authentication-Results header.
/// Operational guarantee: production MTA must add Authentication-Results with dmarc=pass
/// for the customer domain and strip any client-supplied Authentication-Results headers.
/// Untrusted (missing or non-pass) must not allow appending to existing tickets.
/// </summary>
public sealed record EmailAuthenticationResult(
    bool DmarcPassed,
    bool SpfPassed,
    bool DkimPassed,
    string? RawHeader);

/// <summary>
/// Parses Authentication-Results header for DMARC/SPF/DKIM pass signals.
/// Minimal safe default: only dmarc=pass is considered trusted for reply injection.
/// Case-insensitive substring search; header may contain multiple authserv-ids.
/// </summary>
public static class EmailAuthenticationResultParser
{
    public static EmailAuthenticationResult Parse(string? header)
    {
        if (string.IsNullOrWhiteSpace(header))
        {
            return new EmailAuthenticationResult(false, false, false, header);
        }

        var dmarc = header.Contains("dmarc=pass", StringComparison.OrdinalIgnoreCase);
        var spf = header.Contains("spf=pass", StringComparison.OrdinalIgnoreCase);
        var dkim = header.Contains("dkim=pass", StringComparison.OrdinalIgnoreCase);

        return new EmailAuthenticationResult(dmarc, spf, dkim, header);
    }

    public static bool IsTrusted(string? header) => Parse(header).DmarcPassed;
}
