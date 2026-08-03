namespace VSHelpDesk.Application.Abstractions.Security;

/// <summary>
/// Abstraction for HTML sanitization to prevent XSS attacks in email bodies and user content.
/// </summary>
public interface IHtmlSanitizerService
{
    /// <summary>
    /// Sanitizes dangerous HTML content by removing script tags, iframes, inline event handlers, and javascript: links.
    /// </summary>
    string SanitizeHtml(string inputHtml);

    /// <summary>
    /// Converts HTML content to clean, normalized plain text.
    /// </summary>
    string ToPlainText(string inputHtml);
}
