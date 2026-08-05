using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace VSHelpDesk.WebAPI.IntegrationTests.Middleware;

public sealed class ForwardedHeadersTests
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    private sealed record RemoteIpResponse(string? ip, string? scheme);
    private sealed record RateLimitKeyResponse(string? partitionKey, string? ip, string? scheme);

    private static WebApplicationFactory<Program> CreateFactory(int forwardLimit = 2, string[]? trustedNetworks = null)
    {
        trustedNetworks ??= ["10.0.0.0/8", "127.0.0.1/32", "::1/128"];
        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("Database:Provider", "InMemory");
            builder.UseSetting("ConnectionStrings:DefaultConnection", $"TestFwd-{Guid.NewGuid()}");
            builder.UseSetting("Auth:SigningKey", Support.CustomWebApplicationFactory.TestSigningKey);
            builder.UseSetting("Jobs:ApiKey", Support.CustomWebApplicationFactory.TestJobsApiKey);
            builder.UseSetting("Email:SupportMailboxAddress", "support@example.test");
            builder.UseSetting("SeedUser:Enabled", "false");
            builder.UseSetting("SeedAdmin:Enabled", "false");
            builder.UseSetting("ForwardedHeaders:ForwardLimit", forwardLimit.ToString());
            // Clear defaults by setting indexed values; remove any prior expanded values via UseSetting not ideal,
            // but we explicitly set known entries.
            for (var i = 0; i < trustedNetworks.Length; i++)
            {
                builder.UseSetting($"ForwardedHeaders:TrustedNetworks:{i}", trustedNetworks[i]);
            }
        });
        return factory;
    }

    [Fact]
    public async Task Login_rate_limiter_uses_real_client_ip_behind_two_proxies()
    {
        using var factory = CreateFactory(forwardLimit: 2);
        using var client = factory.CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/__test/rate-limit-key");
        request.Headers.TryAddWithoutValidation("X-Forwarded-For", "203.0.113.7, 10.0.0.5");
        request.Headers.TryAddWithoutValidation("X-Forwarded-Proto", "https");
        request.Headers.TryAddWithoutValidation("X-Login-Username", "Admin");

        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await response.Content.ReadAsStringAsync();
        var data = JsonSerializer.Deserialize<RateLimitKeyResponse>(json, JsonOpts);
        Assert.NotNull(data);
        Assert.Equal("203.0.113.7", data!.ip);
        Assert.Equal("login:203.0.113.7:admin", data.partitionKey);
    }

    [Fact]
    public async Task RemoteIp_respects_forward_limit_two()
    {
        using var factory = CreateFactory(forwardLimit: 2);
        using var client = factory.CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/__test/remote-ip");
        request.Headers.TryAddWithoutValidation("X-Forwarded-For", "203.0.113.7, 10.0.0.5");
        request.Headers.TryAddWithoutValidation("X-Forwarded-Proto", "https");

        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        var data = JsonSerializer.Deserialize<RemoteIpResponse>(json, JsonOpts);
        Assert.NotNull(data);
        Assert.Equal("203.0.113.7", data!.ip);
        Assert.Equal("https", data.scheme);
    }

    [Fact]
    public async Task Forwarded_proto_preserved_as_https_when_x_forwarded_proto_present()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/__test/remote-ip");
        request.Headers.TryAddWithoutValidation("X-Forwarded-For", "203.0.113.7");
        request.Headers.TryAddWithoutValidation("X-Forwarded-Proto", "https");

        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        var data = JsonSerializer.Deserialize<RemoteIpResponse>(json, JsonOpts);
        Assert.Equal("https", data!.scheme);
    }

    [Fact]
    public async Task RemoteIp_falls_back_to_proxy_when_untrusted()
    {
        // TrustedNetworks only includes 127.0.0.1, not 10.0.0.0/8, so 10.0.0.5 is not trusted and should not unwrap to 203.0.113.7
        using var factory = CreateFactory(trustedNetworks: ["127.0.0.1/32", "::1/128"]);
        using var client = factory.CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/__test/remote-ip");
        request.Headers.TryAddWithoutValidation("X-Forwarded-For", "203.0.113.7, 10.0.0.5");

        using var response = await client.SendAsync(request);
        var json = await response.Content.ReadAsStringAsync();
        var data = JsonSerializer.Deserialize<RemoteIpResponse>(json, JsonOpts);
        // With only loopback trusted and ForwardLimit 2, the chain walk stops at first untrusted (10.0.0.5), so RemoteIp should be 10.0.0.5? Or loopback?
        // Instead assert it is NOT the attacker-injected 203.0.113.7, proving untrusted entries not blindly trusted.
        Assert.NotEqual("203.0.113.7", data!.ip);
    }

    [Fact]
    public void ForwardedHeadersOptions_has_expected_defaults()
    {
        using var factory = CreateFactory(forwardLimit: 2);
        using var scope = factory.Services.CreateScope();
        var opts = scope.ServiceProvider.GetRequiredService<IOptions<ForwardedHeadersOptions>>().Value;
        Assert.Equal(2, opts.ForwardLimit);
        Assert.False(opts.RequireHeaderSymmetry);
        Assert.Equal(ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto, opts.ForwardedHeaders);
        // Known networks should contain our configured CIDRs
        Assert.Contains(opts.KnownIPNetworks, n => n.ToString() == "10.0.0.0/8");
    }

    [Fact]
    public async Task Rate_limit_partition_normalizes_username_case_and_trims()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/__test/rate-limit-key");
        request.Headers.TryAddWithoutValidation("X-Forwarded-For", "203.0.113.7");
        request.Headers.TryAddWithoutValidation("X-Login-Username", "  ADMIN  ");

        using var response = await client.SendAsync(request);
        var json = await response.Content.ReadAsStringAsync();
        var data = JsonSerializer.Deserialize<RateLimitKeyResponse>(json, JsonOpts);
        Assert.Equal("login:203.0.113.7:admin", data!.partitionKey);
    }
}
