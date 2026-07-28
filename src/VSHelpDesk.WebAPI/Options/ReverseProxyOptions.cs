namespace VSHelpDesk.WebAPI.Options;

public sealed class ReverseProxyOptions
{
    public const string SectionName = "ReverseProxy";

    public int ForwardLimit { get; init; } = 1;

    public string[] KnownProxies { get; init; } = [];

    public string[] KnownNetworks { get; init; } = [];
}
