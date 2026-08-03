using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using VSHelpDesk.WebAPI.IntegrationTests.Support;

namespace VSHelpDesk.WebAPI.IntegrationTests.Authentication;

public sealed class AuthJwtPipelineTests : IClassFixture<CustomWebApplicationFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly WebApplicationFactory<Program> factory;

    public AuthJwtPipelineTests(CustomWebApplicationFactory factory)
    {
        this.factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("environment", "Development");
            builder.UseEnvironment("Development");
        });
    }

    [Fact]
    public async Task BR014_Me_WithoutToken_Returns401()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_SetsHttpOnlyAuthCookie_AndBodyHasNoAccessToken()
    {
        var (username, password) = GetSeedCredentials();
        using var client = CookieAuthTestHelper.CreateCookieClient(factory);

        using var loginResponse = await CookieAuthTestHelper.LoginAsync(client, username, password);

        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
        var loginJson = await loginResponse.Content.ReadAsStringAsync();
        Assert.DoesNotContain("password", loginJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PasswordHash", loginJson, StringComparison.OrdinalIgnoreCase);

        var body = CookieAuthTestHelper.ParseLoginBody(loginJson);
        Assert.NotNull(body);
        Assert.False(body.HasAccessToken);
        Assert.Equal(username, body.Username);
        Assert.False(string.IsNullOrWhiteSpace(body.FullName));
        Assert.NotEqual(Guid.Empty, body.UserId);

        var setCookies = CookieAuthTestHelper.GetSetCookieHeaders(loginResponse);
        var authCookie = CookieAuthTestHelper.FindSetCookie(
            setCookies,
            CookieAuthTestHelper.AuthCookieName);
        var csrfCookie = CookieAuthTestHelper.FindSetCookie(
            setCookies,
            CookieAuthTestHelper.CsrfCookieName);

        Assert.False(string.IsNullOrWhiteSpace(authCookie), "Expected Set-Cookie for vshd.auth");
        Assert.True(
            CookieAuthTestHelper.HasCookieAttribute(authCookie!, "HttpOnly"),
            "vshd.auth must be HttpOnly");
        Assert.False(string.IsNullOrWhiteSpace(csrfCookie), "Expected Set-Cookie for vshd.csrf");
    }

    [Fact]
    public async Task Me_WithAuthCookie_NoBearer_Returns200()
    {
        var (username, password) = GetSeedCredentials();
        using var client = CookieAuthTestHelper.CreateCookieClient(factory);

        using var loginResponse = await CookieAuthTestHelper.LoginAsync(client, username, password);
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);

        // Cookie jar sends vshd.auth; no Authorization header.
        using var meResponse = await client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.OK, meResponse.StatusCode);
        var me = await meResponse.Content.ReadFromJsonAsync<MePayload>(JsonOptions);
        Assert.NotNull(me);
        Assert.Equal(username, me.Username);
        Assert.False(string.IsNullOrWhiteSpace(me.FullName));
        Assert.NotEqual(Guid.Empty, me.UserId);
    }

    [Fact]
    public async Task Logout_ClearsCookies_MeReturns401()
    {
        var (username, password) = GetSeedCredentials();
        using var client = CookieAuthTestHelper.CreateCookieClient(factory);

        using var loginResponse = await CookieAuthTestHelper.LoginAsync(client, username, password);
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);

        using var meBefore = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, meBefore.StatusCode);

        var csrf = CookieAuthTestHelper.GetCookieValue(
            CookieAuthTestHelper.GetSetCookieHeaders(loginResponse),
            CookieAuthTestHelper.CsrfCookieName);
        Assert.False(string.IsNullOrWhiteSpace(csrf), "Expected vshd.csrf after login for logout CSRF");

        using var logoutRequest = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout");
        logoutRequest.Headers.TryAddWithoutValidation("X-CSRF-Token", csrf);
        using var logoutResponse = await client.SendAsync(logoutRequest);
        Assert.Equal(HttpStatusCode.NoContent, logoutResponse.StatusCode);

        using var meAfter = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, meAfter.StatusCode);
    }

    [Fact]
    public async Task UC001_Login_WrongPassword_Returns401()
    {
        var (username, _) = GetSeedCredentials();

        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync(
            "/api/auth/login",
            new { username, password = "definitely-not-the-seed-password" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains(VSHelpDesk.Application.Common.ApplicationMessages.Auth.InvalidCredentials, body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UC001_Login_UnknownUser_Returns401GenericMessage()
    {
        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync(
            "/api/auth/login",
            new { username = "no-such-user-" + Guid.NewGuid().ToString("N")[..8], password = "any-password" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains(VSHelpDesk.Application.Common.ApplicationMessages.Auth.InvalidCredentials, body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BR015_Login_InactiveUser_Returns401GenericMessage()
    {
        var inactive = await IntegrationTestUser.CreateInactiveAsync(factory.Services);
        try
        {
            using var client = factory.CreateClient();
            using var response = await client.PostAsJsonAsync(
                "/api/auth/login",
                new { username = inactive.Username, password = inactive.Password });

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync();
            Assert.Contains(VSHelpDesk.Application.Common.ApplicationMessages.Auth.InvalidCredentials, body, StringComparison.Ordinal);
        }
        finally
        {
            await IntegrationTestUser.DeleteAsync(factory.Services, inactive.Id);
        }
    }

    [Fact]
    public async Task BR014_Me_WithGarbageBearerToken_Returns401()
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "not-a-jwt");
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task BR014_Me_WithWrongSigningKeyToken_Returns401()
    {
        var token = CreateJwtWithSigningKey(
            "wrong-signing-key-with-at-least-32-bytes!!",
            "VSHelpDesk",
            "VSHelpDesk",
            Guid.NewGuid().ToString());

        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PortalTickets_WithoutToken_Returns401()
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/api/tickets");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Jobs_WithoutApiKey_Returns401()
    {
        using var client = factory.CreateClient();
        using var response = await client.PostAsync("/api/jobs/process-incoming-emails", content: null);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task T5_Jobs_WithWrongApiKey_Returns401()
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/jobs/process-incoming-emails");
        request.Headers.Add("X-Jobs-Api-Key", "definitely-not-the-configured-jobs-api-key!!");
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Jobs_WithValidApiKey_ReturnsOkBoundaryResult()
    {
        using var scope = factory.Services.CreateScope();
        var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var apiKey = configuration["Jobs:ApiKey"];
        Assert.False(string.IsNullOrWhiteSpace(apiKey));

        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/jobs/process-incoming-emails");
        request.Headers.Add("X-Jobs-Api-Key", apiKey);
        using var response = await client.SendAsync(request);

        // Fake fetch + best-effort SMTP ack: 200 when handler completes; 502 only on fetch failure.
        Assert.True(
            response.StatusCode is HttpStatusCode.OK or HttpStatusCode.BadGateway,
            $"Unexpected status {(int)response.StatusCode}");
        if (response.StatusCode == HttpStatusCode.OK)
        {
            var json = await response.Content.ReadAsStringAsync();
            Assert.Contains("fetchedCount", json, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("createdTickets", json, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("quarantined", json, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("retryableFailures", json, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("failures", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("messageIds", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("skippedInvalid", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Password", json, StringComparison.OrdinalIgnoreCase);
        }
    }

    private (string Username, string Password) GetSeedCredentials()
    {
        using var scope = factory.Services.CreateScope();
        var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var username = configuration["SeedUser:Username"];
        var password = configuration["SeedUser:Password"];
        Assert.False(string.IsNullOrWhiteSpace(username), "SeedUser:Username must be configured for integration tests.");
        Assert.False(string.IsNullOrWhiteSpace(password), "SeedUser:Password must be configured for integration tests.");
        return (username!, password!);
    }

    private static string CreateJwtWithSigningKey(
        string signingKey,
        string issuer,
        string audience,
        string subject)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey));
        var token = new JwtSecurityToken(
            issuer,
            audience,
            [new Claim(JwtRegisteredClaimNames.Sub, subject)],
            DateTime.UtcNow.AddMinutes(-1),
            DateTime.UtcNow.AddMinutes(30),
            new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private sealed record MePayload(Guid UserId, string Username, string FullName);
}
