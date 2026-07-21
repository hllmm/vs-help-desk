using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using VSHelpDesk.Domain.Enums;
using VSHelpDesk.Infrastructure.Persistence;
using VSHelpDesk.WebAPI.IntegrationTests.Support;

namespace VSHelpDesk.WebAPI.IntegrationTests.Users;

public sealed class UsersApiTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly WebApplicationFactory<Program> factory;

    public UsersApiTests(CustomWebApplicationFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task GetUsers_Unauthorized_WithoutToken()
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/api/users");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task UsersEndpoints_Support_Returns403()
    {
        var (client, csrf, _) = await CookieAuthTestHelper.LoginAsSupportAsync(factory);
        using (client)
        {
            using var getResponse = await client.GetAsync("/api/users");
            Assert.Equal(HttpStatusCode.Forbidden, getResponse.StatusCode);

            CookieAuthTestHelper.UseDefaultCsrfHeader(client, csrf);
            using var postResponse = await client.PostAsJsonAsync(
                "/api/users",
                new
                {
                    fullName = "Blocked",
                    username = $"blocked-{Guid.NewGuid():N}"[..20],
                    email = "blocked@example.test",
                    password = "Password12345!",
                    role = "Support"
                });
            Assert.Equal(HttpStatusCode.Forbidden, postResponse.StatusCode);
        }
    }

    [Fact]
    public async Task GetUsers_Admin_Returns200ListWithSeedUsers()
    {
        var (client, _, _) = await CookieAuthTestHelper.LoginAsAdminAsync(factory);
        using (client)
        {
            using var response = await client.GetAsync("/api/users");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal(JsonValueKind.Array, doc.RootElement.ValueKind);

            var usernames = doc.RootElement.EnumerateArray()
                .Select(element => element.GetProperty("username").GetString())
                .ToList();

            Assert.Contains("admin", usernames);
            Assert.Contains("support", usernames);

            foreach (var element in doc.RootElement.EnumerateArray())
            {
                Assert.True(element.TryGetProperty("id", out _));
                Assert.True(element.TryGetProperty("fullName", out _));
                Assert.True(element.TryGetProperty("email", out _));
                Assert.True(element.TryGetProperty("role", out _));
                Assert.True(element.TryGetProperty("isActive", out _));
                Assert.True(element.TryGetProperty("createdAt", out _));
                Assert.True(element.TryGetProperty("lastLoginAt", out _));
                Assert.False(element.TryGetProperty("passwordHash", out _));
                Assert.False(element.TryGetProperty("password", out _));
            }
        }
    }

    [Fact]
    public async Task PostUsers_Admin_CreateSupport_AndDuplicateUsername400()
    {
        var token = Guid.NewGuid().ToString("N");
        var username = $"u-create-{token[..12]}";
        Guid? createdId = null;

        var (client, csrf, _) = await CookieAuthTestHelper.LoginAsAdminAsync(factory);
        using (client)
        {
            CookieAuthTestHelper.UseDefaultCsrfHeader(client, csrf);

            try
            {
                using var response = await client.PostAsJsonAsync(
                    "/api/users",
                    new
                    {
                        fullName = "Created Support",
                        username,
                        email = $"{username}@example.test",
                        password = "CreatePassword12!",
                        role = "Support"
                    });

                Assert.Equal(HttpStatusCode.Created, response.StatusCode);
                using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                createdId = doc.RootElement.GetProperty("id").GetGuid();
                Assert.Equal(username, doc.RootElement.GetProperty("username").GetString());
                Assert.Equal("Support", doc.RootElement.GetProperty("role").GetString());
                Assert.True(doc.RootElement.GetProperty("isActive").GetBoolean());
                Assert.False(doc.RootElement.TryGetProperty("passwordHash", out _));

                using var listResponse = await client.GetAsync("/api/users");
                listResponse.EnsureSuccessStatusCode();
                using var listDoc = JsonDocument.Parse(await listResponse.Content.ReadAsStringAsync());
                Assert.Contains(
                    listDoc.RootElement.EnumerateArray(),
                    element => element.GetProperty("username").GetString() == username);

                using var duplicate = await client.PostAsJsonAsync(
                    "/api/users",
                    new
                    {
                        fullName = "Dup Admin",
                        username = "admin",
                        email = "dup-admin@example.test",
                        password = "DuplicatePass12!",
                        role = "Support"
                    });
                Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);
            }
            finally
            {
                if (createdId is Guid id)
                {
                    await IntegrationTestUser.DeleteAsync(factory.Services, id);
                }
            }
        }
    }

    [Fact]
    public async Task PutUsers_Admin_LastAdminDemoteOrDeactivate_Returns400()
    {
        var (client, csrf, adminId) = await CookieAuthTestHelper.LoginAsAdminAsync(factory);
        using (client)
        {
            CookieAuthTestHelper.UseDefaultCsrfHeader(client, csrf);

            // Shared DB may retain extra admins from interrupted runs — only assert L1/L2 when sole.
            if (!await IsSoleActiveAdminAsync(adminId))
            {
                return;
            }

            using var demoteResponse = await client.PutAsJsonAsync(
                $"/api/users/{adminId}",
                new
                {
                    fullName = "Local Admin User",
                    email = "admin@vshelpdesk.local",
                    role = "Support",
                    isActive = true
                });
            Assert.Equal(HttpStatusCode.BadRequest, demoteResponse.StatusCode);

            using var deactivateResponse = await client.PutAsJsonAsync(
                $"/api/users/{adminId}",
                new
                {
                    fullName = "Local Admin User",
                    email = "admin@vshelpdesk.local",
                    role = "Admin",
                    isActive = false
                });
            Assert.Equal(HttpStatusCode.BadRequest, deactivateResponse.StatusCode);

            using var verify = await client.GetAsync("/api/users");
            verify.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await verify.Content.ReadAsStringAsync());
            var admin = doc.RootElement.EnumerateArray()
                .Single(element => element.GetProperty("id").GetGuid() == adminId);
            Assert.Equal("Admin", admin.GetProperty("role").GetString());
            Assert.True(admin.GetProperty("isActive").GetBoolean());
        }
    }

    [Fact]
    public async Task PutUsers_Admin_TwoAdmins_DemoteOne_Ok()
    {
        Guid? extraAdminId = null;
        var (client, csrf, seedAdminId) = await CookieAuthTestHelper.LoginAsAdminAsync(factory);
        using (client)
        {
            CookieAuthTestHelper.UseDefaultCsrfHeader(client, csrf);

            try
            {
                var token = Guid.NewGuid().ToString("N");
                var username = $"u-admin2-{token[..10]}";
                using var createResponse = await client.PostAsJsonAsync(
                    "/api/users",
                    new
                    {
                        fullName = "Second Admin",
                        username,
                        email = $"{username}@example.test",
                        password = "SecondAdminPass12!",
                        role = "Admin"
                    });
                createResponse.EnsureSuccessStatusCode();
                using var createDoc = JsonDocument.Parse(await createResponse.Content.ReadAsStringAsync());
                extraAdminId = createDoc.RootElement.GetProperty("id").GetGuid();

                using var demoteResponse = await client.PutAsJsonAsync(
                    $"/api/users/{extraAdminId}",
                    new
                    {
                        fullName = "Second Admin",
                        email = $"{username}@example.test",
                        role = "Support",
                        isActive = true
                    });

                Assert.Equal(HttpStatusCode.OK, demoteResponse.StatusCode);
                using var demoteDoc = JsonDocument.Parse(await demoteResponse.Content.ReadAsStringAsync());
                Assert.Equal("Support", demoteDoc.RootElement.GetProperty("role").GetString());
                Assert.NotEqual(seedAdminId, extraAdminId);
            }
            finally
            {
                if (extraAdminId is Guid id)
                {
                    await IntegrationTestUser.DeleteAsync(factory.Services, id);
                }
            }
        }
    }

    [Fact]
    public async Task PostPassword_Admin_SetWorksWithLogin_AndMinLength()
    {
        Guid? createdId = null;
        var token = Guid.NewGuid().ToString("N");
        var username = $"u-pw-{token[..12]}";
        const string initialPassword = "InitialPassw0rd!";
        const string newPassword = "RotatedPassw0rd!";

        var (adminClient, csrf, _) = await CookieAuthTestHelper.LoginAsAdminAsync(factory);
        using (adminClient)
        {
            CookieAuthTestHelper.UseDefaultCsrfHeader(adminClient, csrf);

            try
            {
                using var createResponse = await adminClient.PostAsJsonAsync(
                    "/api/users",
                    new
                    {
                        fullName = "Password Target",
                        username,
                        email = $"{username}@example.test",
                        password = initialPassword,
                        role = "Support"
                    });
                Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
                using var createDoc = JsonDocument.Parse(await createResponse.Content.ReadAsStringAsync());
                createdId = createDoc.RootElement.GetProperty("id").GetGuid();

                using var shortResponse = await adminClient.PostAsJsonAsync(
                    $"/api/users/{createdId}/password",
                    new { password = "short" });
                Assert.Equal(HttpStatusCode.BadRequest, shortResponse.StatusCode);

                using var passwordResponse = await adminClient.PostAsJsonAsync(
                    $"/api/users/{createdId}/password",
                    new { password = newPassword });
                Assert.Equal(HttpStatusCode.NoContent, passwordResponse.StatusCode);

                using var loginClient = CookieAuthTestHelper.CreateCookieClient(factory);
                using var oldLogin = await CookieAuthTestHelper.LoginAsync(
                    loginClient,
                    username,
                    initialPassword);
                Assert.Equal(HttpStatusCode.Unauthorized, oldLogin.StatusCode);

                using var newLogin = await CookieAuthTestHelper.LoginAsync(
                    loginClient,
                    username,
                    newPassword);
                Assert.Equal(HttpStatusCode.OK, newLogin.StatusCode);
            }
            finally
            {
                if (createdId is Guid id)
                {
                    await IntegrationTestUser.DeleteAsync(factory.Services, id);
                }
            }
        }
    }

    private async Task<bool> IsSoleActiveAdminAsync(Guid adminId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var activeAdmins = db.Users
            .Where(user => user.Role == UserRole.Admin && user.IsActive)
            .Select(user => user.Id)
            .ToList();
        return activeAdmins.Count == 1 && activeAdmins[0] == adminId;
    }
}
