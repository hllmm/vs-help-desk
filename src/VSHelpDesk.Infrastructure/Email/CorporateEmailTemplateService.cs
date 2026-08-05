using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using VSHelpDesk.Application.Abstractions.Email;
using VSHelpDesk.Application.Abstractions.Security;
using VSHelpDesk.Infrastructure.Security;

namespace VSHelpDesk.Infrastructure.Email;

public sealed class CorporateEmailTemplateService : IEmailTemplateService
{
    private static readonly Regex HtmlTagRegex = new(
        "<[^>]+>",
        RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(100));

    private readonly EmailBrandingOptions _branding;
    private readonly IHtmlSanitizerService _htmlSanitizer;

    public CorporateEmailTemplateService(
        IOptions<EmailBrandingOptions> brandingOptions,
        IHtmlSanitizerService htmlSanitizer)
    {
        _branding = brandingOptions?.Value
            ?? throw new ArgumentNullException(nameof(brandingOptions));
        _htmlSanitizer = htmlSanitizer
            ?? throw new ArgumentNullException(nameof(htmlSanitizer));
    }

    public CorporateEmailTemplateService(IOptions<EmailBrandingOptions> brandingOptions)
        : this(brandingOptions, new HtmlSanitizerService())
    {
    }

    public CorporateEmailTemplateService()
        : this(Options.Create(new EmailBrandingOptions()), new HtmlSanitizerService())
    {
    }

    public string WrapInCorporateTemplate(
        string title,
        string body,
        string? actionUrl = null,
        string? actionText = null,
        bool bodyIsHtml = false)
    {
        var safeTitle = Encode(title);
        var safeBody = bodyIsHtml
            ? _htmlSanitizer.SanitizeHtml(body ?? string.Empty)
            : EncodeMultiline(body);
        var safeCompany = Encode(_branding.CompanyName);
        var safeSystem = Encode(_branding.SystemName);
        var safeEmail = Encode(_branding.SupportEmail);
        var safePhone = Encode(_branding.SupportPhone);
        var safeAddress = Encode(_branding.Address);
        var safeFooter = Encode(_branding.FooterText);

        var builder = new StringBuilder();
        builder.AppendLine("<!DOCTYPE html>");
        builder.AppendLine("<html lang=\"tr\"><head><meta charset=\"UTF-8\">");
        builder.AppendLine("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\">");
        builder.AppendLine($"<title>{safeTitle}</title></head>");
        builder.AppendLine("<body style=\"margin:0;padding:0;background:#f4f6f9;color:#334155;font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Arial,sans-serif;\">");
        builder.AppendLine("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" style=\"padding:20px 0;background:#f4f6f9;\"><tr><td align=\"center\">");
        builder.AppendLine("<table role=\"presentation\" width=\"600\" cellpadding=\"0\" cellspacing=\"0\" style=\"width:100%;max-width:600px;background:#fff;border-radius:8px;overflow:hidden;\">");
        builder.AppendLine($"<tr><td style=\"padding:24px 32px;background:linear-gradient(135deg,{_branding.HeaderGradientStart},{_branding.HeaderGradientEnd});\">");

        if (TryGetHttpsUrl(_branding.LogoUrl, out var logoUrl))
        {
            builder.AppendLine(
                $"<img src=\"{Encode(logoUrl)}\" alt=\"{safeCompany}\" style=\"display:block;max-width:180px;max-height:48px;margin:0 0 12px 0;\">");
        }

        builder.AppendLine($"<div style=\"color:#fff;font-size:20px;font-weight:600;\">{safeCompany}</div>");
        builder.AppendLine($"<div style=\"color:#cbd5e1;font-size:13px;margin-top:4px;\">{safeSystem}</div></td></tr>");
        builder.AppendLine("<tr><td style=\"padding:32px;font-size:15px;line-height:1.6;\">");

        if (!string.IsNullOrWhiteSpace(title))
        {
            builder.AppendLine(
                $"<h2 style=\"margin:0 0 18px;color:{_branding.PrimaryColor};font-size:19px;\">{safeTitle}</h2>");
        }

        builder.AppendLine($"<div>{safeBody}</div>");

        if (TryGetHttpsUrl(actionUrl, out var safeActionUrl) &&
            !string.IsNullOrWhiteSpace(actionText))
        {
            builder.AppendLine(
                $"<div style=\"margin-top:24px;\"><a href=\"{Encode(safeActionUrl)}\" style=\"display:inline-block;background:{_branding.PrimaryColor};color:#fff;text-decoration:none;font-weight:600;padding:12px 24px;border-radius:6px;\">{Encode(actionText)}</a></div>");
        }

        builder.AppendLine("</td></tr><tr><td style=\"padding:24px 32px;background:#f8fafc;border-top:1px solid #e2e8f0;text-align:center;color:#64748b;font-size:12px;line-height:1.6;\">");
        builder.AppendLine($"<div><strong>{safeCompany} Support</strong></div>");
        builder.AppendLine($"<div>{safeEmail}{Separator(safePhone)}{safePhone}</div>");

        if (!string.IsNullOrWhiteSpace(_branding.Address))
        {
            builder.AppendLine($"<div>{safeAddress}</div>");
        }

        if (TryGetHttpsUrl(_branding.WebsiteUrl, out var websiteUrl))
        {
            builder.AppendLine(
                $"<div><a href=\"{Encode(websiteUrl)}\" style=\"color:{_branding.PrimaryColor};\">{Encode(websiteUrl)}</a></div>");
        }

        builder.AppendLine($"<div style=\"margin-top:8px;\">{safeFooter}</div>");
        builder.AppendLine("</td></tr></table></td></tr></table></body></html>");
        return builder.ToString();
    }

    public string GeneratePlainTextAlternative(
        string title,
        string body,
        string? actionUrl = null,
        string? actionText = null)
    {
        var builder = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(title))
        {
            builder.AppendLine($"=== {title.Trim()} ===");
            builder.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(body))
        {
            var plainBody = HtmlTagRegex.IsMatch(body)
                ? _htmlSanitizer.ToPlainText(_htmlSanitizer.SanitizeHtml(body))
                : body;
            builder.AppendLine(Regex.Replace(plainBody, @"[ \t]{2,}", " ").Trim());
            builder.AppendLine();
        }

        if (TryGetHttpsUrl(actionUrl, out var safeActionUrl) &&
            !string.IsNullOrWhiteSpace(actionText))
        {
            builder.AppendLine($"[{actionText.Trim()}]: {safeActionUrl}");
            builder.AppendLine();
        }

        builder.AppendLine("---");
        builder.AppendLine($"{_branding.CompanyName} Support");
        builder.AppendLine($"Email: {_branding.SupportEmail}");

        if (!string.IsNullOrWhiteSpace(_branding.SupportPhone))
        {
            builder.AppendLine($"Phone: {_branding.SupportPhone.Trim()}");
        }

        if (!string.IsNullOrWhiteSpace(_branding.Address))
        {
            builder.AppendLine(_branding.Address.Trim());
        }

        if (TryGetHttpsUrl(_branding.WebsiteUrl, out var websiteUrl))
        {
            builder.AppendLine(websiteUrl);
        }

        builder.AppendLine(_branding.FooterText);
        return builder.ToString();
    }

    private static string Encode(string? value) =>
        WebUtility.HtmlEncode(value ?? string.Empty);

    private static string EncodeMultiline(string? value) =>
        Encode(value)
            .Replace("\r\n", "<br>", StringComparison.Ordinal)
            .Replace("\n", "<br>", StringComparison.Ordinal);

    private static string Separator(string value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : " &nbsp;|&nbsp; ";

    private static bool TryGetHttpsUrl(string? value, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(value) ||
            !Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) ||
            !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        normalized = uri.AbsoluteUri;
        return true;
    }
}
