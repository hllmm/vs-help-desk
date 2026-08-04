
using System.Net.Mail;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using VSHelpDesk.Application.Abstractions.Email;

namespace VSHelpDesk.Infrastructure.Email;

public sealed class EmailBrandingOptionsValidator : IValidateOptions<EmailBrandingOptions>
{
    private static readonly Regex HexColorRegex = new(
        @"^#(?:[0-9a-fA-F]{3}){1,2}$",
        RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(100));

    public ValidateOptionsResult Validate(string? name, EmailBrandingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var failures = new List<string>();

        Required(options.CompanyName, nameof(options.CompanyName), failures);
        Required(options.SystemName, nameof(options.SystemName), failures);
        Required(options.SupportEmail, nameof(options.SupportEmail), failures);
        Required(options.FooterText, nameof(options.FooterText), failures);

        ValidateColor(options.PrimaryColor, nameof(options.PrimaryColor), failures);
        ValidateColor(options.HeaderGradientStart, nameof(options.HeaderGradientStart), failures);
        ValidateColor(options.HeaderGradientEnd, nameof(options.HeaderGradientEnd), failures);

        if (!string.IsNullOrWhiteSpace(options.SupportEmail) &&
            !MailAddress.TryCreate(options.SupportEmail.Trim(), out _))
        {
            failures.Add("SupportEmail must be a valid email address.");
        }

        if (options.RequireLogo && string.IsNullOrWhiteSpace(options.LogoUrl))
        {
            failures.Add("LogoUrl is required when RequireLogo is true.");
        }

        ValidateHttpsUrl(options.LogoUrl, nameof(options.LogoUrl), failures);
        ValidateHttpsUrl(options.WebsiteUrl, nameof(options.WebsiteUrl), failures);

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static void Required(string? value, string propertyName, ICollection<string> failures)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            failures.Add($"{propertyName} must not be empty.");
        }
    }

    private static void ValidateColor(string? value, string propertyName, ICollection<string> failures)
    {
        if (!HexColorRegex.IsMatch(value ?? string.Empty))
        {
            failures.Add($"{propertyName} must be a valid hex color.");
        }
    }

    private static void ValidateHttpsUrl(string? value, string propertyName, ICollection<string> failures)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) ||
            !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            failures.Add($"{propertyName} must be an absolute HTTPS URL.");
        }
    }
}
