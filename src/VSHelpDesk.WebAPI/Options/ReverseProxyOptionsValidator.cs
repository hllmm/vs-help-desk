using System.Net;
using Microsoft.Extensions.Options;

namespace VSHelpDesk.WebAPI.Options;

public sealed class ReverseProxyOptionsValidator(string environmentName)
    : IValidateOptions<ReverseProxyOptions>
{
    public ValidateOptionsResult Validate(string? name, ReverseProxyOptions options)
    {
        var failures = new List<string>();

        if (options.ForwardLimit is < 1 or > 4)
        {
            failures.Add("ReverseProxy:ForwardLimit must be between 1 and 4.");
        }

        foreach (var proxy in options.KnownProxies ?? [])
        {
            if (!IPAddress.TryParse(proxy, out _))
            {
                failures.Add($"ReverseProxy:KnownProxies contains an invalid IP address: '{proxy}'.");
            }
        }

        foreach (var network in options.KnownNetworks ?? [])
        {
            if (!IPNetwork.TryParse(network, out _))
            {
                failures.Add($"ReverseProxy:KnownNetworks contains an invalid CIDR network: '{network}'.");
            }
        }

        if (string.Equals(
                environmentName,
                Environments.Production,
                StringComparison.OrdinalIgnoreCase) &&
            (options.KnownProxies?.Length ?? 0) == 0 &&
            (options.KnownNetworks?.Length ?? 0) == 0)
        {
            failures.Add(
                "At least one trusted proxy or network must be configured in Production.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
