using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using VSHelpDesk.Application.Features.Parameters;
using VSHelpDesk.WebAPI.IntegrationTests.Support;

namespace VSHelpDesk.WebAPI.IntegrationTests.Authentication;

public sealed class CsrfProtectionTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly WebApplicationFactory<Program> factory;

    public CsrfProtectionTests(CustomWebApplicationFactory factory)
    {
        this.factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("environment", "Development");
            builder.UseEnvironment("Development");
        });
    }

    [Fact]
    public async Task MutatingRequest_WithAuthCookie_WithoutCsrfHeader_Returns403()
    {
        var (username, password) = GetSeedCredentials();
        using var client = CookieAuthTestHelper.CreateCookieClient(factory);

        using var loginResponse = await CookieAuthTestHelper.LoginAsync(client, username, password);
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);

        // Cookie jar sends vshd.auth + vshd.csrf; omit X-CSRF-Token.
        using var response = await client.PutAsJsonAsync(
            $"/api/parameters/{ApplicationParameterCatalog.AutoResolveInactiveDaysKey}",
            new { value = "5" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("CSRF", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("StackTrace", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Exception", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MutatingRequest_WithMatchingCsrf_SucceedsPastCsrfGate()
    {
        var (username, password) = GetSeedCredentials();
        using var client = CookieAuthTestHelper.CreateCookieClient(factory);

        using var loginResponse = await CookieAuthTestHelper.LoginAsync(client, username, password);
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);

        var csrf = CookieAuthTestHelper.GetCookieValue(
            CookieAuthTestHelper.GetSetCookieHeaders(loginResponse),
            CookieAuthTestHelper.CsrfCookieName);
        Assert.False(string.IsNullOrWhiteSpace(csrf), "Expected vshd.csrf cookie value after login");

        // Unknown parameter key → past CSRF, then 404 (no shared state mutation).
        using var request = new HttpRequestMessage(
            HttpMethod.Put,
            "/api/parameters/nope.Nope")
        {
            Content = JsonContent.Create(new { value = "1" })
        };
        request.Headers.TryAddWithoutValidation("X-CSRF-Token", csrf);

        using var response = await client.SendAsync(request);

        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Logout_WithoutCookies_Returns204()
    {
        // Anonymous logout is a no-op cookie clear; CSRF is not required without vshd.auth.
        using var client = factory.CreateClient();
        using var response = await client.PostAsync("/api/auth/logout", content: null);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Logout_WithAuthCookie_WithoutCsrfHeader_Returns403()
    {
        var (username, password) = GetSeedCredentials();
        using var client = CookieAuthTestHelper.CreateCookieClient(factory);

        using var loginResponse = await CookieAuthTestHelper.LoginAsync(client, username, password);
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);

        // Cookie jar sends vshd.auth + vshd.csrf; omit X-CSRF-Token.
        using var response = await client.PostAsync("/api/auth/logout", content: null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("CSRF", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task JobsEndpoint_WithoutCsrf_StillAcceptsApiKey()
    {
        using var scope = factory.Services.CreateScope();
        var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var apiKey = configuration["Jobs:ApiKey"];
        Assert.False(string.IsNullOrWhiteSpace(apiKey));

        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/jobs/resolve-inactive-tickets");
        request.Headers.Add("X-Jobs-Api-Key", apiKey);
        // No X-CSRF-Token, no auth cookies.

        using var response = await client.SendAsync(request);

        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
        // Valid API key should reach the handler (200) rather than fail auth (401).
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private (string Username, string Password) GetSeedCredentials()
    {
        using var scope = factory.Services.CreateScope();
        var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var username = configuration["SeedUser:Username"];
        var password = configuration["SeedUser:Password"];
        Assert.False(string.IsNullOrWhiteSpace(username), "SeedUser:Username must be configured.");
        Assert.False(string.IsNullOrWhiteSpace(password), "SeedUser:Password must be configured.");
        return (username!, password!);
    }
}
