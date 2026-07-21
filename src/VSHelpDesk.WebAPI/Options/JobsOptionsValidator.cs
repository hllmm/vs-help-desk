using Microsoft.Extensions.Options;

namespace VSHelpDesk.WebAPI.Options;

public sealed class JobsOptionsValidator : IValidateOptions<JobsOptions>
{
    public static readonly string[] ForbiddenPlaceholders =
    [
        "dev-jobs-api-key-change-me",
        "CHANGE_ME",
        "changeme"
    ];

    public ValidateOptionsResult Validate(string? name, JobsOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            return ValidateOptionsResult.Fail(
                "The Jobs:ApiKey configuration value is required (use user-secrets or environment variables).");
        }

        if (options.ApiKey.Trim().Length < 16)
        {
            return ValidateOptionsResult.Fail(
                "The Jobs:ApiKey configuration value must be at least 16 characters.");
        }

        if (ForbiddenPlaceholders.Any(placeholder =>
                options.ApiKey.Contains(placeholder, StringComparison.OrdinalIgnoreCase)))
        {
            return ValidateOptionsResult.Fail(
                "The Jobs:ApiKey configuration value must not use a committed placeholder; set a private key via user-secrets or environment.");
        }

        return ValidateOptionsResult.Success;
    }
}
