using System.Text;
using Microsoft.Extensions.Options;

namespace VSHelpDesk.Infrastructure.Authentication;

public sealed class AuthOptionsValidator : IValidateOptions<AuthOptions>
{
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

        if (Encoding.UTF8.GetByteCount(options.SigningKey) < 32)
        {
            failures.Add("The Auth:SigningKey configuration value must contain at least 32 UTF-8 bytes.");
        }

        if (options.ExpirationMinutes <= 0)
        {
            failures.Add("The Auth:ExpirationMinutes configuration value must be positive.");
        }

        return failures;
    }
}
