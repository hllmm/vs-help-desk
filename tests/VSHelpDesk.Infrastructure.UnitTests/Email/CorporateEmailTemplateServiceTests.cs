using Microsoft.Extensions.Options;
using VSHelpDesk.Application.Abstractions.Email;
using VSHelpDesk.Infrastructure.Email;

namespace VSHelpDesk.Infrastructure.UnitTests.Email;

public sealed class CorporateEmailTemplateServiceTests
{
    [Fact]
    public void WrapInCorporateTemplate_UsesConfiguredBrandingOptions()
    {
        var branding = Options.Create(new EmailBrandingOptions
        {
            CompanyName = "ACME Corp",
            SystemName = "Support Portal",
            PrimaryColor = "#ff0000",
            SupportEmail = "help@acme.com",
            SupportPhone = "+1 (555) 0199",
            FooterText = "ACME Confidential"
        });

        var service = new CorporateEmailTemplateService(branding);
        var html = service.WrapInCorporateTemplate("Ticket Created", "Your ticket has been received.");

        Assert.Contains("ACME Corp", html);
        Assert.Contains("Support Portal", html);
        Assert.Contains("#ff0000", html);
        Assert.Contains("help@acme.com", html);
        Assert.Contains("+1 (555) 0199", html);
        Assert.Contains("ACME Confidential", html);
    }

    [Fact]
    public void WrapInCorporateTemplate_EncodesHtmlTitleAndBody()
    {
        var service = new CorporateEmailTemplateService();
        var html = service.WrapInCorporateTemplate("<script>alert('xss')</script>", "Plain text body");

        Assert.DoesNotContain("<script>alert('xss')</script>", html);
        Assert.Contains("&lt;script&gt;alert(&#39;xss&#39;)&lt;/script&gt;", html);
    }

    [Fact]
    public void GeneratePlainTextAlternative_RendersCleanText()
    {
        var branding = Options.Create(new EmailBrandingOptions
        {
            CompanyName = "ACME Corp",
            SupportEmail = "help@acme.com",
            SupportPhone = "+1 (555) 0199",
            FooterText = "ACME Confidential"
        });

        var service = new CorporateEmailTemplateService(branding);
        var plainText = service.GeneratePlainTextAlternative(
            "Ticket Update",
            "Your ticket status is now Resolved.",
            "https://portal.acme.com/tickets/123",
            "View Ticket");

        Assert.Contains("=== Ticket Update ===", plainText);
        Assert.Contains("Your ticket status is now Resolved.", plainText);
        Assert.Contains("[View Ticket]: https://portal.acme.com/tickets/123", plainText);
        Assert.Contains("ACME Corp Support", plainText);
        Assert.Contains("Email: help@acme.com", plainText);
    }

    [Fact]
    public void WrapInCorporateTemplate_WithoutLogo_DoesNotRenderImage()
    {
        var service = new CorporateEmailTemplateService(
            Options.Create(new EmailBrandingOptions { LogoUrl = null }));

        var html = service.WrapInCorporateTemplate("Ticket", "Body");

        Assert.DoesNotContain("<img", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WrapInCorporateTemplate_WithHttpsLogo_RendersConfiguredImage()
    {
        var service = new CorporateEmailTemplateService(
            Options.Create(new EmailBrandingOptions
            {
                CompanyName = "ACME Corp",
                LogoUrl = "https://cdn.example.test/acme-logo.png"
            }));

        var html = service.WrapInCorporateTemplate("Ticket", "Body");

        Assert.Contains("<img", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("https://cdn.example.test/acme-logo.png", html);
        Assert.Contains("alt=\"ACME Corp\"", html);
    }

    [Fact]
    public void WrapInCorporateTemplate_HtmlBody_SanitizesDangerousMarkup()
    {
        var service = new CorporateEmailTemplateService();

        var html = service.WrapInCorporateTemplate(
            "Ticket",
            "<p>Hello <strong>world</strong></p><script>alert(1)</script>",
            bodyIsHtml: true);

        Assert.Contains("<strong>world</strong>", html);
        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("alert(1)", html, StringComparison.Ordinal);
    }
}
