using System.Text.RegularExpressions;

namespace VSHelpDesk.Infrastructure.Logging;

/// <summary>
/// Default implementation of <see cref="ILogPropertySanitizer"/> that masks passwords,
/// secrets, connection string credentials, and Bearer tokens.
/// </summary>
public sealed class LogPropertySanitizer : ILogPropertySanitizer
{
    private static readonly Regex PasswordJsonRegex = new(
        @"""(password|pass|secret|token|apiKey|signingKey)""\s*:\s*""[^""]+""",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ConnectionStringPasswordRegex = new(
        @"(Password|Pwd|Secret)\s*=\s*[^;]+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex BearerHeaderRegex = new(
        @"Bearer\s+[A-Za-z0-9\-\._~\+\/]+=*",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public string? Sanitize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return text;
        }

        var result = text;

        // Mask JSON password/secret properties
        result = PasswordJsonRegex.Replace(result, m =>
        {
            var key = m.Value[..m.Value.IndexOf(':')];
            return $"{key}: \"***MASKED***\"";
        });

        // Mask Connection String passwords
        result = ConnectionStringPasswordRegex.Replace(result, m =>
        {
            var key = m.Value[..m.Value.IndexOf('=')];
            return $"{key}=***MASKED***";
        });

        // Mask Bearer Tokens
        result = BearerHeaderRegex.Replace(result, "Bearer ***MASKED***");

        return result;
    }
}
