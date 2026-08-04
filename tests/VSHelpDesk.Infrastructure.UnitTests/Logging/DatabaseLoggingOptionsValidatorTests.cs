using VSHelpDesk.Infrastructure.Logging;

namespace VSHelpDesk.Infrastructure.UnitTests.Logging;

public sealed class DatabaseLoggingOptionsValidatorTests
{
    private readonly DatabaseLoggingOptionsValidator _validator = new();

    [Fact]
    public void Validate_ValidOptions_ReturnsSuccess()
    {
        var options = new DatabaseLoggingOptions
        {
            BatchSize = 100,
            RetentionDays = 30,
            QueueCapacity = 1000
        };

        var result = _validator.Validate(null, options);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Validate_InvalidBatchSize_ReturnsFail()
    {
        var options = new DatabaseLoggingOptions
        {
            BatchSize = 0,
            RetentionDays = 30,
            QueueCapacity = 1000
        };

        var result = _validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures, f => f.Contains("BatchSize"));
    }

    [Fact]
    public void Validate_InvalidRetentionDays_ReturnsFail()
    {
        var options = new DatabaseLoggingOptions
        {
            BatchSize = 100,
            RetentionDays = 0,
            QueueCapacity = 1000
        };

        var result = _validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures, f => f.Contains("RetentionDays"));
    }
}
