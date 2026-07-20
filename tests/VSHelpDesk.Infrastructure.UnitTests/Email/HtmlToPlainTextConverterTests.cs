using VSHelpDesk.Application.Features.MailProcessing;
using VSHelpDesk.Infrastructure.Email;

namespace VSHelpDesk.Infrastructure.UnitTests.Email;

public sealed class HtmlToPlainTextConverterTests
{
    private readonly HtmlToPlainTextConverter converter = new();

    [Fact]
    public void HtmlConverter_RemovesScriptStyleAndNoscript_DecodesEntities()
    {
        var html =
            "<html><body>" +
            "<p>Hello&nbsp;&amp;&lt;world&gt;</p>" +
            "<script>alert('xss')</script>" +
            "<style>.secret{color:red}</style>" +
            "<noscript>enable js</noscript>" +
            "</body></html>";

        var plain = converter.Convert(html);

        Assert.Contains("Hello", plain, StringComparison.Ordinal);
        Assert.Contains("&", plain, StringComparison.Ordinal);
        Assert.Contains("<world>", plain, StringComparison.Ordinal);
        Assert.DoesNotContain("alert", plain, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret", plain, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("enable js", plain, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<script", plain, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void HtmlConverter_PreservesBlockAndBreakBoundaries()
    {
        var html = "<p>First</p><div>Second<br>Third</div><h1>Title</h1><li>Item</li>";

        var plain = converter.Convert(html);

        Assert.Contains("First", plain, StringComparison.Ordinal);
        Assert.Contains("Second", plain, StringComparison.Ordinal);
        Assert.Contains("Third", plain, StringComparison.Ordinal);
        Assert.Contains("Title", plain, StringComparison.Ordinal);
        Assert.Contains("Item", plain, StringComparison.Ordinal);

        var firstIndex = plain.IndexOf("First", StringComparison.Ordinal);
        var secondIndex = plain.IndexOf("Second", StringComparison.Ordinal);
        var thirdIndex = plain.IndexOf("Third", StringComparison.Ordinal);
        Assert.True(firstIndex >= 0 && secondIndex > firstIndex);
        Assert.True(thirdIndex > secondIndex);
        Assert.Contains('\n', plain[firstIndex..secondIndex]);
        Assert.Contains('\n', plain[secondIndex..thirdIndex]);
    }

    [Fact]
    public void HtmlConverter_EmptyHtml_UsesEmptyBodyPlaceholder()
    {
        Assert.Equal(InboundMailLimits.EmptyBodyPlaceholder, converter.Convert(null));
        Assert.Equal(InboundMailLimits.EmptyBodyPlaceholder, converter.Convert("   "));
        Assert.Equal(InboundMailLimits.EmptyBodyPlaceholder, converter.Convert("<p></p>"));
    }
}
