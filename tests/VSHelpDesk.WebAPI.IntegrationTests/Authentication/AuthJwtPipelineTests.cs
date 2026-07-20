using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

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
        using var scope = factory.Services.CreateScope();
        var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var username = configuration["SeedUser:Username"];
        var password = configuration["SeedUser:Password"];
        Assert.False(string.IsNullOrWhiteSpace(username), "SeedUser:Username must be configured for integration tests.");
        Assert.False(string.IsNullOrWhiteSpace(password), "SeedUser:Password must be configured for integration tests.");

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
        using var scope = factory.Services.CreateScope();
        var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var username = configuration["SeedUser:Username"] ?? "support";

        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync(
            "/api/auth/login",
            new { username, password = "definitely-not-the-seed-password" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Invalid username or password.", body, StringComparison.Ordinal);
    }

    private sealed record MePayload(Guid UserId, string Username, string FullName);
}
