using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using VSHelpDesk.Application.Features.MailProcessing;

namespace VSHelpDesk.Infrastructure.Email;

/// <summary>
/// Converts untrusted HTML mail bodies into safe plain text for inbound processing.
/// </summary>
public sealed class HtmlToPlainTextConverter
{
    private static readonly Regex HorizontalWhitespace = new(
        @"[^\S\r\n]+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ExcessiveNewlines = new(
        @"\n{3,}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public string Convert(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return InboundMailLimits.NormalizeBody(null);
        }

        var document = new HtmlDocument();
        document.LoadHtml(html);

        foreach (var node in document.DocumentNode.SelectNodes(
                     "//script|//style|//noscript") ?? Enumerable.Empty<HtmlNode>())
        {
            node.Remove();
        }

        foreach (var br in document.DocumentNode.SelectNodes("//br") ?? Enumerable.Empty<HtmlNode>())
        {
            br.ParentNode?.ReplaceChild(
                document.CreateTextNode("\n"),
                br);
        }

        foreach (var block in document.DocumentNode.SelectNodes(
                     "//p|//div|//li|//tr|//h1|//h2|//h3|//h4|//h5|//h6")
                 ?? Enumerable.Empty<HtmlNode>())
        {
            if (block.ParentNode is null)
            {
                continue;
            }

            block.ParentNode.InsertBefore(document.CreateTextNode("\n"), block);
            block.ParentNode.InsertAfter(document.CreateTextNode("\n"), block);
        }

        var rawText = document.DocumentNode.InnerText ?? string.Empty;
        var decoded = HtmlEntity.DeEntitize(rawText);
        // HtmlEntity may leave numeric entities; WebUtility covers residual sequences.
        decoded = WebUtility.HtmlDecode(decoded) ?? string.Empty;

        var normalizedNewlines = decoded
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');

        var collapsed = HorizontalWhitespace.Replace(normalizedNewlines, " ");
        collapsed = ExcessiveNewlines.Replace(collapsed, "\n\n");

        var builder = new StringBuilder(collapsed.Length);
        foreach (var line in collapsed.Split('\n'))
        {
            if (builder.Length > 0)
            {
                builder.Append('\n');
            }

            builder.Append(line.Trim());
        }

        return InboundMailLimits.NormalizeBody(builder.ToString());
    }
}
