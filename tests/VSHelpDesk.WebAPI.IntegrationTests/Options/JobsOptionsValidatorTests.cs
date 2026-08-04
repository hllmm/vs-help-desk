using VSHelpDesk.WebAPI.Options;
using Xunit;

namespace VSHelpDesk.WebAPI.IntegrationTests.Options;

public sealed class JobsOptionsValidatorTests
{
    [Theory]
    [InlineData("dev-jobs-api-key-change-me")]
    [InlineData("replace-with-random-secret-key-1234567890")]
    [InlineData("this-is-a-changeme-secret-key")]
    [InlineData("example-key-that-is-long-enough-12345")]
    public void JobsOptionsValidator_SubstringPlaceholders_FailsValidation(string apiKeyWithPlaceholder)
    {
        var validator = new JobsOptionsValidator();
        var options = new JobsOptions
        {
            ApiKey = apiKeyWithPlaceholder
        };

        var result = validator.Validate(null, options);
        Assert.True(result.Failed);
        Assert.Contains("placeholder", result.FailureMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void JobsOptionsValidator_ValidApiKey_Succeeds()
    {
        var validator = new JobsOptionsValidator();
        var options = new JobsOptions
        {
            ApiKey = "super-secure-production-api-key-9988776655"
        };

        var result = validator.Validate(null, options);
        Assert.True(result.Succeeded);
    }
}
