using System.Net;
using System.Text;
using VSHelpDesk.Application.Abstractions.Email;

namespace VSHelpDesk.Infrastructure.Email;

public sealed class CorporateEmailTemplateService : IEmailTemplateService
{
    public string WrapInCorporateTemplate(
        string title,
        string body,
        string? actionUrl = null,
        string? actionText = null)
    {
        var safeTitle = WebUtility.HtmlEncode(title ?? string.Empty);

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
        sb.AppendLine("<html lang=\"en\">");
        sb.AppendLine("<head>");
        sb.AppendLine("  <meta charset=\"UTF-8\">");
        sb.AppendLine("  <meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\">");
        sb.AppendLine($"  <title>{safeTitle}</title>");
        sb.AppendLine("  <style>");
        sb.AppendLine("    body { margin: 0; padding: 0; font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif; background-color: #f4f6f9; color: #333333; }");
        sb.AppendLine("    .container { max-width: 600px; margin: 0 auto; background-color: #ffffff; border-radius: 8px; overflow: hidden; box-shadow: 0 4px 12px rgba(0,0,0,0.05); }");
        sb.AppendLine("    .header { background: linear-gradient(135deg, #1e293b 0%, #0f172a 100%); padding: 24px 32px; text-align: left; }");
        sb.AppendLine("    .header h1 { color: #ffffff; margin: 0; font-size: 20px; font-weight: 600; letter-spacing: -0.5px; }");
        sb.AppendLine("    .header .subtitle { color: #94a3b8; font-size: 13px; margin-top: 4px; }");
        sb.AppendLine("    .content { padding: 32px; font-size: 15px; line-height: 1.6; color: #334155; }");
        sb.AppendLine("    .content h2 { margin-top: 0; color: #1e293b; font-size: 18px; font-weight: 600; }");
        sb.AppendLine("    .action-button { display: inline-block; background-color: #2563eb; color: #ffffff !important; font-weight: 600; text-decoration: none; padding: 12px 24px; border-radius: 6px; margin-top: 20px; text-align: center; }");
        sb.AppendLine("    .footer { background-color: #f8fafc; padding: 24px 32px; border-top: 1px solid #e2e8f0; font-size: 12px; color: #64748b; text-align: center; line-height: 1.5; }");
        sb.AppendLine("    .footer a { color: #2563eb; text-decoration: none; }");
        sb.AppendLine("  </style>");
        sb.AppendLine("</head>");
        sb.AppendLine("<body>");
        sb.AppendLine("  <table width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" style=\"padding: 20px 0; background-color: #f4f6f9;\">");
        sb.AppendLine("    <tr>");
        sb.AppendLine("      <td align=\"center\">");
        sb.AppendLine("        <div class=\"container\">");
        sb.AppendLine("          <div class=\"header\">");
        sb.AppendLine("            <h1>VS Help Desk</h1>");
        sb.AppendLine("            <div class=\"subtitle\">Corporate Customer Support System</div>");
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
        sb.AppendLine("            <p style=\"margin: 0 0 8px 0;\"><strong>VS Help Desk Corporate Support</strong></p>");
        sb.AppendLine("            <p style=\"margin: 0 0 8px 0;\">Email: support@vshelpdesk.com | Phone: +90 (212) 555-0100</p>");
        sb.AppendLine("            <p style=\"margin: 0;\">&copy; 2026 VS Help Desk. All rights reserved.</p>");
        sb.AppendLine("          </div>");
        sb.AppendLine("        </div>");
        sb.AppendLine("      </td>");
        sb.AppendLine("    </tr>");
        sb.AppendLine("  </table>");
        sb.AppendLine("</body>");
        sb.AppendLine("</html>");

        return sb.ToString();
    }

    private static bool IsHtmlContent(string text)
    {
        var trimmed = text.Trim();
        return trimmed.StartsWith("<") && (trimmed.EndsWith(">") || trimmed.Contains("</"));
    }
}
