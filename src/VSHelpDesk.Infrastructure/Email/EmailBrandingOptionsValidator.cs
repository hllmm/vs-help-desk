using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using VSHelpDesk.Application.Abstractions.Email;

namespace VSHelpDesk.Infrastructure.Email;

/// <summary>
/// Startup validator for <see cref="EmailBrandingOptions"/>.
/// </summary>
public sealed class EmailBrandingOptionsValidator : IValidateOptions<EmailBrandingOptions>
{
    private static readonly Regex HexColorRegex = new(
        @"^#(?:[0-9a-fA-F]{3}){1,2}$",
        RegexOptions.Compiled);

    public ValidateOptionsResult Validate(string? name, EmailBrandingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.CompanyName))
        {
            failures.Add("CompanyName must not be empty.");
        }

        if (string.IsNullOrWhiteSpace(options.SystemName))
        {
            failures.Add("SystemName must not be empty.");
        }

        if (!HexColorRegex.IsMatch(options.PrimaryColor ?? string.Empty))
        {
            failures.Add("PrimaryColor must be a valid hex color (e.g. #2563eb).");
        }

        if (!HexColorRegex.IsMatch(options.HeaderGradientStart ?? string.Empty))
        {
            failures.Add("HeaderGradientStart must be a valid hex color (e.g. #1e293b).");
        }

        if (!HexColorRegex.IsMatch(options.HeaderGradientEnd ?? string.Empty))
        {
            failures.Add("HeaderGradientEnd must be a valid hex color (e.g. #0f172a).");
        }

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }
}
