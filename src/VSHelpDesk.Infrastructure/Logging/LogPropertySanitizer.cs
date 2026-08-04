
using System.Text.RegularExpressions;

namespace VSHelpDesk.Infrastructure.Logging;

public sealed class LogPropertySanitizer : ILogPropertySanitizer
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(100);
    private static readonly Regex JsonSecretRegex = new(
        "\\\"(password|pass|pwd|secret|token|accessToken|refreshToken|apiKey|signingKey|csrfToken|cookie|authorization|smtpPassword|imapPassword)\\\"\\s*:\\s*\\\"[^\\\"]*\\\"",
        RegexOptions.IgnoreCase | RegexOptions.Compiled,
        RegexTimeout);
    private static readonly Regex ConnectionSecretRegex = new(
        @"(Password|Pwd|Secret|AccessToken)\s*=\s*[^;\s]+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled,
        RegexTimeout);
    private static readonly Regex BearerRegex = new(
        @"Bearer\s+[A-Za-z0-9\-\._~\+\/]+=*",
        RegexOptions.IgnoreCase | RegexOptions.Compiled,
        RegexTimeout);
    private static readonly Regex SensitiveHeaderRegex = new(
        @"(?im)^(Authorization|Cookie|Set-Cookie|X-CSRF-Token)\s*:\s*.*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled,
        RegexTimeout);
    private static readonly Regex SensitiveQueryRegex = new(
        @"(?<prefix>[?&](?:access_token|refresh_token|token|api_key|apikey|password|csrf)\s*=)[^&#\s]*",
        RegexOptions.IgnoreCase | RegexOptions.Compiled,
        RegexTimeout);

    public string? Sanitize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return text;
        try
        {
            var result = JsonSecretRegex.Replace(text, match =>
            {
                var separator = match.Value.IndexOf(':');
                return $"{match.Value[..separator]}: \"***MASKED***\"";
            });
            result = ConnectionSecretRegex.Replace(result, match =>
            {
                var separator = match.Value.IndexOf('=');
                return $"{match.Value[..separator]}=***MASKED***";
            });
            result = BearerRegex.Replace(result, "Bearer ***MASKED***");
            result = SensitiveHeaderRegex.Replace(result, match =>
            {
                var headerName = match.Groups[1].Value;
                var maskedValue =
                    headerName.Equals("Authorization", StringComparison.OrdinalIgnoreCase) &&
                    match.Value.Contains("Bearer", StringComparison.OrdinalIgnoreCase)
                        ? "Bearer ***MASKED***"
                        : "***MASKED***";
                return $"{headerName}: {maskedValue}";
            });
            result = SensitiveQueryRegex.Replace(result, "${prefix}***MASKED***");
            return result;
        }
        catch (RegexMatchTimeoutException)
        {
            return "***SANITIZATION_TIMEOUT***";
        }
    }
}
