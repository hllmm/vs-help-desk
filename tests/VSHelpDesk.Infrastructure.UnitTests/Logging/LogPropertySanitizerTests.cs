using VSHelpDesk.Infrastructure.Logging;

namespace VSHelpDesk.Infrastructure.UnitTests.Logging;

public sealed class LogPropertySanitizerTests
{
    private readonly LogPropertySanitizer _sanitizer = new();

    [Fact]
    public void Sanitize_MasksPasswordInJson()
    {
        var raw = "{\"username\": \"admin\", \"password\": \"SuperSecret123!\"}";
        var sanitized = _sanitizer.Sanitize(raw);

        Assert.NotNull(sanitized);
        Assert.DoesNotContain("SuperSecret123!", sanitized);
        Assert.Contains("***MASKED***", sanitized);
    }

    [Fact]
    public void Sanitize_MasksConnectionStringPassword()
    {
        var raw = "Host=localhost;Database=vshd;Password=MyDbPassword;Port=5432";
        var sanitized = _sanitizer.Sanitize(raw);

        Assert.NotNull(sanitized);
        Assert.DoesNotContain("MyDbPassword", sanitized);
        Assert.Contains("Password=***MASKED***", sanitized);
    }

    [Fact]
    public void Sanitize_MasksBearerTokens()
    {
        var raw = "Authorization: Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiIxMjM0NTY3ODkwIn0.signature";
        var sanitized = _sanitizer.Sanitize(raw);

        Assert.NotNull(sanitized);
        Assert.DoesNotContain("eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9", sanitized);
        Assert.Contains("Bearer ***MASKED***", sanitized);
    }
}
