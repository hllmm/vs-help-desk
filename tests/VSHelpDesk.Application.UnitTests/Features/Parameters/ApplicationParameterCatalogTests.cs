using VSHelpDesk.Application.Features.Parameters;

namespace VSHelpDesk.Application.UnitTests.Features.Parameters;

public sealed class ApplicationParameterCatalogTests
{
    [Fact]
    public void Validates_inactive_days_range()
    {
        Assert.True(ApplicationParameterCatalog.TryValidate(
            ApplicationParameterCatalog.AutoResolveInactiveDaysKey, "3", out _));
        Assert.False(ApplicationParameterCatalog.TryValidate(
            ApplicationParameterCatalog.AutoResolveInactiveDaysKey, "0", out var code));
        Assert.Equal(ParameterCodes.ValueInvalid, code);
        Assert.False(ApplicationParameterCatalog.TryValidate(
            ApplicationParameterCatalog.AutoResolveInactiveDaysKey, "31", out _));
        Assert.False(ApplicationParameterCatalog.TryValidate(
            "not.a.key", "1", out var unknown));
        Assert.Equal(ParameterCodes.KeyUnknown, unknown);
    }
}
