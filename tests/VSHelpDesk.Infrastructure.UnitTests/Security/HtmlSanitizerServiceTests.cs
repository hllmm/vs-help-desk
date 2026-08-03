using VSHelpDesk.Infrastructure.Security;
using Xunit;

namespace VSHelpDesk.Infrastructure.UnitTests.Security;

public sealed class HtmlSanitizerServiceTests
{
    private readonly HtmlSanitizerService _sanitizer = new();

    [Theory]
    [InlineData("<script>alert('XSS')</script>", "")]
    [InlineData("<script type=\"text/javascript\">alert('XSS')</script><p>Hello</p>", "<p>Hello</p>")]
    [InlineData("<iframe src=\"http://malicious.test\"></iframe>", "")]
    [InlineData("<object data=\"malicious.swf\"></object>", "")]
    [InlineData("<embed src=\"malicious.swf\">", "")]
    [InlineData("<style>body { display: none; }</style>", "")]
    public void SanitizeHtml_RemovesDangerousTags(string input, string expected)
    {
        var sanitized = _sanitizer.SanitizeHtml(input);
        Assert.Equal(expected, sanitized);
    }

    [Theory]
    [InlineData("<img src=\"valid.jpg\" onload=\"alert('XSS')\" />", "<img src=\"valid.jpg\">")]
    [InlineData("<button onclick=\"alert('XSS')\">Click</button>", "<button>Click</button>")]
    [InlineData("<div onmouseover=\"badCode()\">Hover me</div>", "<div>Hover me</div>")]
    [InlineData("<a href=\"valid.html\" onerror=\"badCode()\">Link</a>", "<a href=\"valid.html\">Link</a>")]
    public void SanitizeHtml_RemovesInlineEventHandlers(string input, string expected)
    {
        var sanitized = _sanitizer.SanitizeHtml(input);
        Assert.Equal(expected, sanitized);
    }

    [Theory]
    [InlineData("<a href=\"javascript:alert('XSS')\">Click</a>", "<a>Click</a>")]
    [InlineData("<a href=\"  JAVASCRIPT:alert('XSS')  \">Click</a>", "<a>Click</a>")]
    [InlineData("<a href=\"vbscript:msgbox('XSS')\">Click</a>", "<a>Click</a>")]
    [InlineData("<a href=\"data:text/html;base64,PHNjcmlwdD5hbGVydCgxKTwvc2NyaXB0Pg==\">Click</a>", "<a>Click</a>")]
    public void SanitizeHtml_RemovesJavascriptAndDangerousLinks(string input, string expected)
    {
        var sanitized = _sanitizer.SanitizeHtml(input);
        Assert.Equal(expected, sanitized);
    }

    [Fact]
    public void SanitizeHtml_PreservesSafeHtml()
    {
        var safeHtml = "<p>Welcome to <b>VS HelpDesk</b>. Please visit <a href=\"https://example.test/help\">Help Center</a>.</p>";
        var sanitized = _sanitizer.SanitizeHtml(safeHtml);
        Assert.Equal(safeHtml, sanitized);
    }

    [Fact]
    public void ToPlainText_ConvertsHtmlToCleanPlainText()
    {
        var html = "<h1>Title</h1><p>Paragraph 1</p><p>Paragraph 2 <br/>Line 2</p>";
        var plainText = _sanitizer.ToPlainText(html);

        Assert.Contains("Title", plainText);
        Assert.Contains("Paragraph 1", plainText);
        Assert.Contains("Paragraph 2", plainText);
        Assert.DoesNotContain("<h1>", plainText);
        Assert.DoesNotContain("<p>", plainText);
    }
}
