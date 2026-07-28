using Microsoft.Extensions.Hosting;
using VSHelpDesk.WebAPI.Options;

namespace VSHelpDesk.WebAPI.IntegrationTests.Infrastructure;

public sealed class ReverseProxyOptionsTests
{
    [Fact]
    public void ProductionWithoutTrustList_Fails()
    {
        var result = Validate(
            Environments.Production,
            new ReverseProxyOptions { ForwardLimit = 1 });

        Assert.True(result.Failed);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    public void ForwardLimitOutsideOneThroughFour_Fails(int value)
    {
        var result = Validate(
            Environments.Production,
            new ReverseProxyOptions
            {
                ForwardLimit = value,
                KnownProxies = ["172.30.0.10"]
            });

        Assert.True(result.Failed);
    }

    [Theory]
    [InlineData("not-an-ip")]
    [InlineData("172.30.0.999")]
    public void MalformedKnownProxy_Fails(string value)
    {
        var result = Validate(
            Environments.Production,
            new ReverseProxyOptions
            {
                KnownProxies = [value]
            });

        Assert.True(result.Failed);
    }

    [Theory]
    [InlineData("not-a-network")]
    [InlineData("10.244.0.0/99")]
    [InlineData("10.244.0.0")]
    public void MalformedKnownNetwork_Fails(string value)
    {
        var result = Validate(
            Environments.Production,
            new ReverseProxyOptions
            {
                KnownNetworks = [value]
            });

        Assert.True(result.Failed);
    }

    [Fact]
    public void DockerProxyConfiguration_IsAccepted()
    {
        var result = Validate(
            Environments.Production,
            new ReverseProxyOptions
            {
                ForwardLimit = 1,
                KnownProxies = ["172.30.0.10"]
            });

        Assert.False(result.Failed);
    }

    [Fact]
    public void KubernetesNetworkConfiguration_IsAccepted()
    {
        var result = Validate(
            Environments.Production,
            new ReverseProxyOptions
            {
                ForwardLimit = 2,
                KnownNetworks = ["10.244.0.0/16"]
            });

        Assert.False(result.Failed);
    }

    [Fact]
    public void DevelopmentWithoutTrustList_IsAccepted()
    {
        var result = Validate(
            Environments.Development,
            new ReverseProxyOptions());

        Assert.False(result.Failed);
    }

    private static Microsoft.Extensions.Options.ValidateOptionsResult Validate(
        string environmentName,
        ReverseProxyOptions options) =>
        new ReverseProxyOptionsValidator(environmentName).Validate(null, options);
}
