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
/// Uses exact token boundaries (\bdmarc=pass\b) and optional trusted authserv-id.
/// </summary>
public static class EmailAuthenticationResultParser
{
    public static EmailAuthenticationResult Parse(string? header, string? trustedAuthServId = null)
    {
        if (string.IsNullOrWhiteSpace(header))
        {
            return new EmailAuthenticationResult(false, false, false, header);
        }

        // Exact token boundaries: \bdmarc=pass\b etc, not substring inside xdmarc=passfoo
        var dmarc = System.Text.RegularExpressions.Regex.IsMatch(header, @"\bdmarc=pass\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        var spf = System.Text.RegularExpressions.Regex.IsMatch(header, @"\bspf=pass\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        var dkim = System.Text.RegularExpressions.Regex.IsMatch(header, @"\bdkim=pass\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        // If trusted authserv-id configured, header's first token (authserv-id) must match exactly
        if (!string.IsNullOrWhiteSpace(trustedAuthServId))
        {
            var authServId = ExtractAuthServId(header);
            if (!string.Equals(authServId, trustedAuthServId.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                dmarc = false; // untrusted if authserv-id mismatch, even if dmarc=pass present
            }
        }

        return new EmailAuthenticationResult(dmarc, spf, dkim, header);
    }

    public static bool IsTrusted(string? header, string? trustedAuthServId = null) => Parse(header, trustedAuthServId).DmarcPassed;

    private static string? ExtractAuthServId(string header)
    {
        var trimmed = header.Trim();
        if (trimmed.Length == 0) return null;
        // Authserv-id is first token before ';' or whitespace
        var semi = trimmed.IndexOf(';');
        var end = semi >= 0 ? semi : trimmed.IndexOf(' ');
        if (end < 0) end = trimmed.IndexOf('\t');
        if (end < 0) end = trimmed.Length;
        var id = trimmed[..end].Trim();
        // Remove trailing whitespace/comments
        return string.IsNullOrWhiteSpace(id) ? null : id;
    }
}
