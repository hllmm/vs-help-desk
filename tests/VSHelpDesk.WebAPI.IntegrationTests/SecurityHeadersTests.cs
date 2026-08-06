using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

using VSHelpDesk.WebAPI.IntegrationTests.Support;

namespace VSHelpDesk.WebAPI.IntegrationTests;

public sealed class SecurityHeadersTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly WebApplicationFactory<Program> factory;

    public SecurityHeadersTests(CustomWebApplicationFactory factory)
    {
        this.factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
        });
    }

    [Fact]
    public async Task Health_returns_csp_header()
    {
        using var client = factory.CreateClient();
        using var resp = await client.GetAsync("/health");

        Assert.True(resp.Headers.Contains("Content-Security-Policy"), "Missing CSP header");
        var csp = string.Join(" ", resp.Headers.GetValues("Content-Security-Policy"));
        Assert.Contains("default-src 'self'", csp);
        Assert.Contains("object-src 'none'", csp);
        Assert.Contains("base-uri 'none'", csp);
        Assert.Contains("frame-ancestors 'none'", csp);
        Assert.Contains("script-src 'self'", csp);
        Assert.Contains("style-src 'self'", csp);
        Assert.Contains("img-src 'self' data:", csp);
        Assert.Contains("connect-src 'self'", csp);
        Assert.Contains("form-action 'self'", csp);
    }

    [Fact]
    public async Task Web_returns_csp_header_on_root()
    {
        using var client = factory.CreateClient();
        // Follow redirects disabled? just get root — may be 404 but headers must still be present
        using var resp = await client.GetAsync("/");
        Assert.True(resp.Headers.Contains("Content-Security-Policy"));
        var csp = string.Join(" ", resp.Headers.GetValues("Content-Security-Policy"));
        Assert.Contains("default-src 'self'", csp);
        Assert.Contains("object-src 'none'", csp);
    }

    [Fact]
    public async Task Response_contains_permissions_policy_and_hardened_headers()
    {
        using var client = factory.CreateClient();
        using var resp = await client.GetAsync("/health");

        // Permissions-Policy
        Assert.True(resp.Headers.Contains("Permissions-Policy"), "Missing Permissions-Policy");
        var pp = string.Join(" ", resp.Headers.GetValues("Permissions-Policy"));
        Assert.Contains("camera=()", pp);
        Assert.Contains("microphone=()", pp);
        Assert.Contains("geolocation=()", pp);

        // X-Content-Type-Options
        Assert.True(resp.Headers.Contains("X-Content-Type-Options"));
        Assert.Contains("nosniff", string.Join(" ", resp.Headers.GetValues("X-Content-Type-Options")), StringComparison.OrdinalIgnoreCase);

        // X-Frame-Options
        Assert.True(resp.Headers.Contains("X-Frame-Options"));
        Assert.Contains("DENY", string.Join(" ", resp.Headers.GetValues("X-Frame-Options")), StringComparison.OrdinalIgnoreCase);

        // Referrer-Policy
        Assert.True(resp.Headers.Contains("Referrer-Policy"));
        Assert.Contains("strict-origin-when-cross-origin", string.Join(" ", resp.Headers.GetValues("Referrer-Policy")), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Api_unauthorized_still_returns_security_headers()
    {
        using var client = factory.CreateClient();
        using var resp = await client.GetAsync("/api/tickets");
        // 401 is expected without auth, but headers must still be present
        Assert.True(resp.Headers.Contains("Content-Security-Policy"));
        Assert.True(resp.Headers.Contains("Permissions-Policy"));
    }
}
