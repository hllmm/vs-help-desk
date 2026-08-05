using VSHelpDesk.Infrastructure.Authentication;

namespace VSHelpDesk.Infrastructure.UnitTests.Authentication;

public sealed class AuthOptionsValidatorTests
{
    [Theory]
    [InlineData(0, false)]
    [InlineData(10, false)]
    [InlineData(61, false)]
    [InlineData(480, false)]
    [InlineData(60, true)]
    [InlineData(30, true)]
    [InlineData(15, true)]
    public void ExpirationMinutes_outside_15_to_60_fails(int minutes, bool shouldPass)
    {
        var opts = new AuthOptions { Issuer = "VSHelpDesk", Audience = "VSHelpDesk", SigningKey = new string('x', 32), ExpirationMinutes = minutes };
        var validator = new AuthOptionsValidator();
        var result = validator.Validate(null, opts);
        var passed = !result.Failed;
        Assert.Equal(shouldPass, passed);
    }

    [Theory]
    [InlineData(10)]
    [InlineData(61)]
    [InlineData(480)]
    public void ExpirationMinutes_outside_bounds_produces_correct_message(int minutes)
    {
        var opts = new AuthOptions { Issuer = "VSHelpDesk", Audience = "VSHelpDesk", SigningKey = new string('x', 32), ExpirationMinutes = minutes };
        var validator = new AuthOptionsValidator();
        var result = validator.Validate(null, opts);
        Assert.True(result.Failed);
        Assert.Contains(result.Failures, f => f.Contains("between 15 and 60", StringComparison.Ordinal));
    }

    [Fact]
    public void ExpirationMinutes_zero_fails_with_positive_message()
    {
        var opts = new AuthOptions { Issuer = "VSHelpDesk", Audience = "VSHelpDesk", SigningKey = new string('x', 32), ExpirationMinutes = 0 };
        var validator = new AuthOptionsValidator();
        var result = validator.Validate(null, opts);
        Assert.True(result.Failed);
        Assert.Contains(result.Failures, f => f.Contains("positive", StringComparison.OrdinalIgnoreCase) || f.Contains("between 15 and 60", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(15)]
    [InlineData(30)]
    [InlineData(60)]
    public void ExpirationMinutes_inside_bounds_passes(int minutes)
    {
        var opts = new AuthOptions { Issuer = "VSHelpDesk", Audience = "VSHelpDesk", SigningKey = new string('x', 32), ExpirationMinutes = minutes };
        var validator = new AuthOptionsValidator();
        var result = validator.Validate(null, opts);
        Assert.False(result.Failed);
    }
}
