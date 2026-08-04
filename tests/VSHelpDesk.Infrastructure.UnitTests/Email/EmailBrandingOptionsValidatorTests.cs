using VSHelpDesk.Application.Abstractions.Email;
using VSHelpDesk.Infrastructure.Email;

namespace VSHelpDesk.Infrastructure.UnitTests.Email;

public sealed class EmailBrandingOptionsValidatorTests
{
    private readonly EmailBrandingOptionsValidator _validator = new();

    [Fact]
    public void Validate_ValidOptions_ReturnsSuccess()
    {
        var options = new EmailBrandingOptions
        {
            CompanyName = "My Company",
            SystemName = "Help Desk",
            PrimaryColor = "#2563eb",
            HeaderGradientStart = "#1e293b",
            HeaderGradientEnd = "#0f172a"
        };

        var result = _validator.Validate(null, options);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Validate_InvalidColor_ReturnsFail()
    {
        var options = new EmailBrandingOptions
        {
            CompanyName = "My Company",
            SystemName = "Help Desk",
            PrimaryColor = "blue"
        };

        var result = _validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures, f => f.Contains("PrimaryColor"));
    }
}
