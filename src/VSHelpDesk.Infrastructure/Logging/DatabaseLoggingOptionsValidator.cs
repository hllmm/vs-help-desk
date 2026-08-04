using Microsoft.Extensions.Options;

namespace VSHelpDesk.Infrastructure.Logging;

/// <summary>
/// Validates <see cref="DatabaseLoggingOptions"/> at startup.
/// </summary>
public sealed class DatabaseLoggingOptionsValidator : IValidateOptions<DatabaseLoggingOptions>
{
    public ValidateOptionsResult Validate(string? name, DatabaseLoggingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();

        if (options.BatchSize is < 1 or > 1000)
        {
            failures.Add("BatchSize must be between 1 and 1000.");
        }

        if (options.RetentionDays is < 1 or > 365)
        {
            failures.Add("RetentionDays must be between 1 and 365.");
        }

        if (options.QueueCapacity is < 10 or > 50000)
        {
            failures.Add("QueueCapacity must be between 10 and 50000.");
        }

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }
}
