using System.Net;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;
using VSHelpDesk.WebAPI.Options;

namespace VSHelpDesk.WebAPI.Extensions;

public static class ForwardedHeadersExtensions
{
    public static IServiceCollection AddTrustedForwardedHeaders(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        services.AddSingleton<IValidateOptions<ReverseProxyOptions>>(
            new ReverseProxyOptionsValidator(environment.EnvironmentName));
        services.AddOptions<ReverseProxyOptions>()
            .Bind(configuration.GetSection(ReverseProxyOptions.SectionName))
            .ValidateOnStart();

        services.AddOptions<ForwardedHeadersOptions>()
            .Configure<IOptions<ReverseProxyOptions>>((options, configured) =>
            {
                var reverseProxy = configured.Value;
                options.ForwardedHeaders =
                    ForwardedHeaders.XForwardedFor |
                    ForwardedHeaders.XForwardedProto;
                options.ForwardLimit = reverseProxy.ForwardLimit;
                options.KnownIPNetworks.Clear();
                options.KnownProxies.Clear();

                foreach (var proxy in reverseProxy.KnownProxies)
                {
                    options.KnownProxies.Add(IPAddress.Parse(proxy));
                }

                foreach (var network in reverseProxy.KnownNetworks)
                {
                    options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(network));
                }
            });

        return services;
    }
}
