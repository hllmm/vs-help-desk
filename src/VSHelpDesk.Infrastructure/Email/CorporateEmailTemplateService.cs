using System.Net;
using System.Text;
using Microsoft.Extensions.Options;
using VSHelpDesk.Application.Abstractions.Email;

namespace VSHelpDesk.Infrastructure.Email;

public sealed class CorporateEmailTemplateService : IEmailTemplateService
{
    private readonly EmailBrandingOptions _brandingOptions;

    public CorporateEmailTemplateService(IOptions<EmailBrandingOptions>? brandingOptions = null)
    {
        _brandingOptions = brandingOptions?.Value ?? new EmailBrandingOptions();
    }

    public string WrapInCorporateTemplate(
        string title,
        string body,
        string? actionUrl = null,
        string? actionText = null)
    {
        var safeTitle = WebUtility.HtmlEncode(title ?? string.Empty);
        var safeCompanyName = WebUtility.HtmlEncode(_brandingOptions.CompanyName);
        var safeSystemName = WebUtility.HtmlEncode(_brandingOptions.SystemName);
        var safeSupportEmail = WebUtility.HtmlEncode(_brandingOptions.SupportEmail);
        var safeSupportPhone = WebUtility.HtmlEncode(_brandingOptions.SupportPhone);
        var safeFooterText = WebUtility.HtmlEncode(_brandingOptions.FooterText);

        string formattedBody;
        if (string.IsNullOrWhiteSpace(body))
        {
            formattedBody = string.Empty;
        }
        else if (IsHtmlContent(body))
        {
            formattedBody = body;
        }
        else
        {
            formattedBody = WebUtility.HtmlEncode(body).Replace("\r\n", "<br />").Replace("\n", "<br />");
        }

        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html lang=\"tr\">");
        sb.AppendLine("<head>");
        sb.AppendLine("  <meta charset=\"UTF-8\">");
        sb.AppendLine("  <meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\">");
        sb.AppendLine($"  <title>{safeTitle}</title>");
        sb.AppendLine("  <style>");
        sb.AppendLine($"    body {{ margin: 0; padding: 0; font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif; background-color: #f4f6f9; color: #333333; }}");
        sb.AppendLine("    .container { max-width: 600px; margin: 0 auto; background-color: #ffffff; border-radius: 8px; overflow: hidden; box-shadow: 0 4px 12px rgba(0,0,0,0.05); }");
        sb.AppendLine($"    .header {{ background: linear-gradient(135deg, {_brandingOptions.HeaderGradientStart} 0%, {_brandingOptions.HeaderGradientEnd} 100%); padding: 24px 32px; text-align: left; }}");
        sb.AppendLine("    .header h1 { color: #ffffff; margin: 0; font-size: 20px; font-weight: 600; letter-spacing: -0.5px; }");
        sb.AppendLine("    .header .subtitle { color: #94a3b8; font-size: 13px; margin-top: 4px; }");
        sb.AppendLine("    .content { padding: 32px; font-size: 15px; line-height: 1.6; color: #334155; }");
        sb.AppendLine("    .content h2 { margin-top: 0; color: #1e293b; font-size: 18px; font-weight: 600; }");
        sb.AppendLine($"    .action-button {{ display: inline-block; background-color: {_brandingOptions.PrimaryColor}; color: #ffffff !important; font-weight: 600; text-decoration: none; padding: 12px 24px; border-radius: 6px; margin-top: 20px; text-align: center; }}");
        sb.AppendLine("    .footer { background-color: #f8fafc; padding: 24px 32px; border-top: 1px solid #e2e8f0; font-size: 12px; color: #64748b; text-align: center; line-height: 1.5; }");
        sb.AppendLine($"    .footer a {{ color: {_brandingOptions.PrimaryColor}; text-decoration: none; }}");
        sb.AppendLine("  </style>");
        sb.AppendLine("</head>");
        sb.AppendLine("<body>");
        sb.AppendLine("  <table width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" style=\"padding: 20px 0; background-color: #f4f6f9;\">");
        sb.AppendLine("    <tr>");
        sb.AppendLine("      <td align=\"center\">");
        sb.AppendLine("        <div class=\"container\">");
        sb.AppendLine("          <div class=\"header\">");

        if (!string.IsNullOrWhiteSpace(_brandingOptions.LogoUrl))
        {
            var safeLogoUrl = WebUtility.HtmlEncode(_brandingOptions.LogoUrl);
            sb.AppendLine($"            <img src=\"{safeLogoUrl}\" alt=\"{safeCompanyName}\" style=\"max-height: 40px; margin-bottom: 12px;\" />");
        }

        sb.AppendLine($"            <h1>{safeCompanyName}</h1>");
        sb.AppendLine($"            <div class=\"subtitle\">{safeSystemName}</div>");
        sb.AppendLine("          </div>");
        sb.AppendLine("          <div class=\"content\">");

        if (!string.IsNullOrWhiteSpace(safeTitle))
        {
            sb.AppendLine($"            <h2>{safeTitle}</h2>");
        }

        sb.AppendLine($"            <div>{formattedBody}</div>");

        if (!string.IsNullOrWhiteSpace(actionUrl) && !string.IsNullOrWhiteSpace(actionText))
        {
            var safeUrl = WebUtility.HtmlEncode(actionUrl);
            var safeText = WebUtility.HtmlEncode(actionText);
            sb.AppendLine($"            <div style=\"margin-top: 24px;\"><a href=\"{safeUrl}\" class=\"action-button\">{safeText}</a></div>");
        }

        sb.AppendLine("          </div>");
        sb.AppendLine("          <div class=\"footer\">");
        sb.AppendLine($"            <p style=\"margin: 0 0 8px 0;\"><strong>{safeCompanyName} Support</strong></p>");
        sb.AppendLine($"            <p style=\"margin: 0 0 8px 0;\">Email: {safeSupportEmail} | Phone: {safeSupportPhone}</p>");
        sb.AppendLine($"            <p style=\"margin: 0;\">{safeFooterText}</p>");
        sb.AppendLine("          </div>");
        sb.AppendLine("        </div>");
        sb.AppendLine("      </td>");
        sb.AppendLine("    </tr>");
        sb.AppendLine("  </table>");
        sb.AppendLine("</body>");
        sb.AppendLine("</html>");

        return sb.ToString();
    }

    public string GeneratePlainTextAlternative(
        string title,
        string body,
        string? actionUrl = null,
        string? actionText = null)
    {
        var sb = new StringBuilder();

        if (!string.IsNullOrWhiteSpace(title))
        {
            sb.AppendLine($"=== {title.Trim()} ===");
            sb.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(body))
        {
            var plainBody = IsHtmlContent(body) ? StripTags(body) : body.Trim();
            sb.AppendLine(plainBody);
            sb.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(actionUrl) && !string.IsNullOrWhiteSpace(actionText))
        {
            sb.AppendLine($"[{actionText.Trim()}]: {actionUrl.Trim()}");
            sb.AppendLine();
        }

        sb.AppendLine("---");
        sb.AppendLine($"{_brandingOptions.CompanyName} Support");
        sb.AppendLine($"Email: {_brandingOptions.SupportEmail} | Phone: {_brandingOptions.SupportPhone}");
        sb.AppendLine(_brandingOptions.FooterText);

        return sb.ToString();
    }

    private static bool IsHtmlContent(string text)
    {
        var trimmed = text.Trim();
        return trimmed.StartsWith("<") && (trimmed.EndsWith(">") || trimmed.Contains("</"));
    }

    private static string StripTags(string html)
    {
        return System.Text.RegularExpressions.Regex.Replace(html, "<.*?>", string.Empty);
    }
}
