using System.Net;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using VSHelpDesk.Application.Abstractions.Security;
using VSHelpDesk.Infrastructure.Email;

namespace VSHelpDesk.Infrastructure.Security;

/// <summary>
/// Implements HTML sanitization and XSS prevention using HtmlAgilityPack rules.
/// Sanitizes dangerous script tags, iframes, inline event handlers, and javascript: links.
/// </summary>
public sealed class HtmlSanitizerService : IHtmlSanitizerService
{
    private static readonly HashSet<string> DangerousTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "script", "iframe", "object", "embed", "applet", "style", "link",
        "meta", "form", "base"
    };

    private static readonly HashSet<string> UrlAttributes = new(StringComparer.OrdinalIgnoreCase)
    {
        "href", "src", "action", "formaction", "xlink:href", "data"
    };

    private readonly HtmlToPlainTextConverter _plainTextConverter = new();

    public string SanitizeHtml(string inputHtml)
    {
        if (string.IsNullOrWhiteSpace(inputHtml))
        {
            return string.Empty;
        }

        var doc = new HtmlDocument();
        doc.LoadHtml(inputHtml);

        // 1. Remove dangerous nodes completely
        var nodesToRemove = doc.DocumentNode.Descendants()
            .Where(node => DangerousTags.Contains(node.Name))
            .ToList();

        foreach (var node in nodesToRemove)
        {
            node.Remove();
        }

        // 2. Clean remaining attributes
        var allNodes = doc.DocumentNode.DescendantsAndSelf().ToList();
        foreach (var node in allNodes)
        {
            if (!node.HasAttributes)
            {
                continue;
            }

            var attributesToRemove = new List<HtmlAttribute>();
            foreach (var attr in node.Attributes)
            {
                var attrName = attr.Name.Trim();

                // Remove inline event handlers (onload, onclick, onerror, etc.)
                if (attrName.StartsWith("on", StringComparison.OrdinalIgnoreCase))
                {
                    attributesToRemove.Add(attr);
                    continue;
                }

                // Check URL attributes for javascript:, vbscript:, data: protocols
                if (UrlAttributes.Contains(attrName))
                {
                    var decodedVal = WebUtility.HtmlDecode(attr.Value ?? string.Empty).Trim();
                    // Strip control chars and whitespace inside URI scheme
                    decodedVal = Regex.Replace(decodedVal, @"\s+", "");

                    if (decodedVal.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase) ||
                        decodedVal.StartsWith("vbscript:", StringComparison.OrdinalIgnoreCase) ||
                        decodedVal.StartsWith("data:text/html", StringComparison.OrdinalIgnoreCase))
                    {
                        attributesToRemove.Add(attr);
                    }
                }
            }

            foreach (var attr in attributesToRemove)
            {
                node.Attributes.Remove(attr);
            }
        }

        return doc.DocumentNode.WriteTo();
    }

    public string ToPlainText(string inputHtml)
    {
        return _plainTextConverter.Convert(inputHtml);
    }
}
