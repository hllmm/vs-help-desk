using System.Text;
using Microsoft.Extensions.Options;

namespace VSHelpDesk.Infrastructure.Authentication;

public sealed class AuthOptionsValidator : IValidateOptions<AuthOptions>
{
    public static readonly string[] ForbiddenSigningKeyPlaceholders =
    [
        "CHANGE_ME_DEV_ONLY_MIN_32_CHARS_LONG!!",
        "local-dev-only-signing-key",
        "CHANGE_ME",
        "changeme",
        "replace-with",
        "example",
        "dummy",
        "local-only"
    ];

    public ValidateOptionsResult Validate(string? name, AuthOptions options)
    {
        var failures = GetFailures(options);
        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    public static void ThrowIfInvalid(AuthOptions options)
    {
        var failures = GetFailures(options);
        if (failures.Count > 0)
        {
            throw new InvalidOperationException(failures[0]);
        }
    }

    private static List<string> GetFailures(AuthOptions options)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.Issuer))
        {
            failures.Add("The Auth:Issuer configuration value is required.");
        }

        if (string.IsNullOrWhiteSpace(options.Audience))
        {
            failures.Add("The Auth:Audience configuration value is required.");
        }

        if (string.IsNullOrWhiteSpace(options.SigningKey))
        {
            failures.Add(
                "The Auth:SigningKey configuration value is required (set via user-secrets or environment variables).");
        }
        else if (Encoding.UTF8.GetByteCount(options.SigningKey) < 32)
        {
            failures.Add("The Auth:SigningKey configuration value must contain at least 32 UTF-8 bytes.");
        }
        else if (ForbiddenSigningKeyPlaceholders.Any(placeholder =>
                     options.SigningKey.Contains(placeholder, StringComparison.OrdinalIgnoreCase)))
        {
            failures.Add(
                "The Auth:SigningKey configuration value must not use a committed placeholder; set a private key via user-secrets or environment.");
        }

        if (options.ExpirationMinutes <= 0)
        {
            failures.Add("The Auth:ExpirationMinutes configuration value must be positive.");
        }

        return failures;
    }
}
