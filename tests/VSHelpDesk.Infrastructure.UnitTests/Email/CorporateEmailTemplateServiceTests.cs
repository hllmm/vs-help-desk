using VSHelpDesk.Infrastructure.Email;

namespace VSHelpDesk.Infrastructure.UnitTests.Email;

public sealed class CorporateEmailTemplateServiceTests
{
    private readonly CorporateEmailTemplateService _service = new();

    [Fact]
    public void WrapInCorporateTemplate_WithTitleAndBody_ReturnsValidHtmlStructure()
    {
        var title = "Ticket #1001 Confirmation";
        var body = "Your ticket has been logged successfully.";

        var html = _service.WrapInCorporateTemplate(title, body);

        Assert.Contains("<!DOCTYPE html>", html);
        Assert.Contains("<title>Ticket #1001 Confirmation</title>", html);
        Assert.Contains("VS Help Desk", html);
        Assert.Contains("Your ticket has been logged successfully.", html);
        Assert.Contains("support@vshelpdesk.com", html);
    }

    [Fact]
    public void WrapInCorporateTemplate_WithActionUrlAndText_IncludesActionButton()
    {
        var title = "Action Required";
        var body = "Please click below to view your ticket.";
        var actionUrl = "https://helpdesk.example.com/tickets/1001";
        var actionText = "View Ticket";

        var html = _service.WrapInCorporateTemplate(title, body, actionUrl, actionText);

        Assert.Contains("href=\"https://helpdesk.example.com/tickets/1001\"", html);
        Assert.Contains("View Ticket", html);
        Assert.Contains("class=\"action-button\"", html);
    }

    [Fact]
    public void WrapInCorporateTemplate_PlainTextBody_ConvertsNewlinesToBreakTags()
    {
        var title = "Multi-line Test";
        var body = "Line 1\r\nLine 2\nLine 3";

        var html = _service.WrapInCorporateTemplate(title, body);

        Assert.Contains("Line 1<br />Line 2<br />Line 3", html);
    }

    [Fact]
    public void WrapInCorporateTemplate_HtmlBody_PreservesHtmlMarkup()
    {
        var title = "HTML Content Test";
        var body = "<p>This is <strong>bold</strong> text.</p>";

        var html = _service.WrapInCorporateTemplate(title, body);

        Assert.Contains("<p>This is <strong>bold</strong> text.</p>", html);
    }

    [Fact]
    public void WrapInCorporateTemplate_SpecialCharacters_EncodesTitleAndPlainText()
    {
        var title = "Alert <script>alert(1)</script>";
        var body = "User input & test <bad>";

        var html = _service.WrapInCorporateTemplate(title, body);

        Assert.DoesNotContain("<script>", html);
        Assert.Contains("&lt;script&gt;", html);
        Assert.Contains("User input &amp; test &lt;bad&gt;", html);
    }
}
