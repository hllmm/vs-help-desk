using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace VSHelpDesk.WebAPI.IntegrationTests.Authentication;

public sealed class LoginRateLimitIntegrationTests : IClassFixture<LoginRateLimitIntegrationTests.ProductionFactory>
{
    public sealed class ProductionFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("Database:Provider", "InMemory");
            builder.UseSetting("ConnectionStrings:DefaultConnection", $"TestRateLimitDb-{Guid.NewGuid()}");
            builder.UseSetting("Auth:SigningKey", "production-test-signing-key-with-at-least-32-bytes!!");
            builder.UseSetting("Auth:Issuer", "VSHelpDesk");
            builder.UseSetting("Auth:Audience", "VSHelpDesk");
            builder.UseSetting("Auth:ExpirationMinutes", "60");
            builder.UseSetting("Jobs:ApiKey", "production-test-jobs-api-key-32!!");
            builder.UseSetting("Email:ReceiverMode", "Imap");
            builder.UseSetting("Email:SmtpHost", "smtp.example.local");
            builder.UseSetting("Email:SmtpPort", "587");
            builder.UseSetting("Email:SmtpSecurityMode", "StartTls");
            builder.UseSetting("Email:ImapHost", "imap.example.local");
            builder.UseSetting("Email:ImapPort", "993");
            builder.UseSetting("Email:ImapSecurityMode", "SslOnConnect");
            builder.UseSetting("Email:ImapUsername", "imap-user");
            builder.UseSetting("Email:ImapPassword", "imap-pass");
            builder.UseSetting("Email:ImapAccountId", "account-id");
            builder.UseSetting("Email:ImapFolder", "INBOX");
            builder.UseSetting("Email:SupportMailboxAddress", "support@example.test");
            builder.UseSetting("Email:SupportMailboxDisplayName", "VS Help Desk");
            builder.UseSetting("Email:TrustedAuthServId", "mx.test");
            builder.UseSetting("FileStorage:RootPath", "storage");
            builder.UseSetting("ForwardedHeaders:ForwardLimit", "2");
            builder.UseSetting("ForwardedHeaders:TrustedNetworks:0", "127.0.0.1/32");
            builder.UseSetting("ForwardedHeaders:TrustedNetworks:1", "::1/128");
            builder.UseSetting("ForwardedHeaders:TrustedNetworks:2", "10.0.0.0/8");
        }
    }

    private readonly WebApplicationFactory<Program> _factory;

    public LoginRateLimitIntegrationTests(ProductionFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Login_WithDifferentUserHeaders_FromSameIp_ShouldBeRateLimitedAfterTenAttempts()
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        const string clientIp = "203.0.113.7";

        // Send ten failed login attempts each with a different X-Login-Username header from the same IP.
        for (var i = 0; i < 10; i++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login");
            request.Headers.TryAddWithoutValidation("X-Forwarded-For", clientIp);
            request.Headers.TryAddWithoutValidation("X-Forwarded-Proto", "https");
            request.Headers.TryAddWithoutValidation("X-Login-Username", $"user{i}");
            request.Content = JsonContent.Create(new { username = $"user{i}", password = "wrong-password" });

            using var response = await client.SendAsync(request);

            // First 10 should NOT be rate-limited (401 invalid credentials, not 429).
            Assert.NotEqual(HttpStatusCode.TooManyRequests, response.StatusCode);
        }

        // Eleventh request with a new username from the same IP must be rate-limited.
        using var eleventh = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login");
        eleventh.Headers.TryAddWithoutValidation("X-Forwarded-For", clientIp);
        eleventh.Headers.TryAddWithoutValidation("X-Forwarded-Proto", "https");
        eleventh.Headers.TryAddWithoutValidation("X-Login-Username", "user10");
        eleventh.Content = JsonContent.Create(new { username = "user10", password = "wrong-password" });

        using var eleventhResponse = await client.SendAsync(eleventh);

        Assert.Equal(HttpStatusCode.TooManyRequests, eleventhResponse.StatusCode);
    }
}
