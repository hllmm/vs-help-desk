using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using VSHelpDesk.Domain.Entities;
using VSHelpDesk.Infrastructure.Persistence;

namespace VSHelpDesk.WebAPI.IntegrationTests.Authentication;

public sealed class AuthJwtPipelineTests : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly WebApplicationFactory<Program> factory;

    public AuthJwtPipelineTests(WebApplicationFactory<Program> factory)
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
    public async Task UC001_Login_ThenMe_WithBearerToken_Returns200AndUserSummary()
    {
        var (username, password) = GetSeedCredentials();

        using var client = factory.CreateClient();
        using var loginResponse = await client.PostAsJsonAsync(
            "/api/auth/login",
            new { username, password });

        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
        var loginJson = await loginResponse.Content.ReadAsStringAsync();
        Assert.DoesNotContain("password", loginJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PasswordHash", loginJson, StringComparison.OrdinalIgnoreCase);

        using var loginDocument = JsonDocument.Parse(loginJson);
        var accessToken = loginDocument.RootElement.GetProperty("accessToken").GetString();
        Assert.False(string.IsNullOrWhiteSpace(accessToken));

        using var meRequest = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        meRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var meResponse = await client.SendAsync(meRequest);

        Assert.Equal(HttpStatusCode.OK, meResponse.StatusCode);
        var me = await meResponse.Content.ReadFromJsonAsync<MePayload>(JsonOptions);
        Assert.NotNull(me);
        Assert.Equal(username, me.Username);
        Assert.False(string.IsNullOrWhiteSpace(me.FullName));
        Assert.NotEqual(Guid.Empty, me.UserId);
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
        Assert.Contains("Invalid username or password.", body, StringComparison.Ordinal);
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
        Assert.Contains("Invalid username or password.", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BR015_Login_InactiveUser_Returns401GenericMessage()
    {
        var (username, password) = GetSeedCredentials();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var user = await db.Users.SingleAsync(candidate => candidate.Username == username);
        user.Deactivate();
        await db.SaveChangesAsync();

        try
        {
            using var client = factory.CreateClient();
            using var response = await client.PostAsJsonAsync(
                "/api/auth/login",
                new { username, password });

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync();
            Assert.Contains("Invalid username or password.", body, StringComparison.Ordinal);
        }
        finally
        {
            // Reactivate seed user for other tests in the same process.
            await using var cleanupScope = factory.Services.CreateAsyncScope();
            var cleanupDb = cleanupScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var inactive = await cleanupDb.Users.SingleAsync(candidate => candidate.Username == username);
            cleanupDb.Entry(inactive).Property(nameof(User.IsActive)).CurrentValue = true;
            cleanupDb.Entry(inactive).Property(nameof(User.IsActive)).IsModified = true;
            await cleanupDb.SaveChangesAsync();
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

        // Day 8: handler runs (Fake fetch + optional SMTP probe). 200 if Mailpit up; 502 if SMTP down.
        Assert.True(
            response.StatusCode is HttpStatusCode.OK or HttpStatusCode.BadGateway,
            $"Unexpected status {(int)response.StatusCode}");
        if (response.StatusCode == HttpStatusCode.OK)
        {
            var json = await response.Content.ReadAsStringAsync();
            Assert.Contains("fetchedCount", json, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("messageIds", json, StringComparison.OrdinalIgnoreCase);
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
