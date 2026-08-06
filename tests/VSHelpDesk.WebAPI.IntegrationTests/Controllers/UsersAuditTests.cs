using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using VSHelpDesk.Infrastructure.Persistence;
using VSHelpDesk.WebAPI.IntegrationTests.Support;

namespace VSHelpDesk.WebAPI.IntegrationTests.Controllers;

public sealed class UsersAuditTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly WebApplicationFactory<Program> factory;

    public UsersAuditTests(CustomWebApplicationFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task CreateUser_emits_audit_event()
    {
        var token = Guid.NewGuid().ToString("N");
        var username = $"u-audit-c-{token[..12]}";
        Guid? createdId = null;

        var (client, csrf, adminId) = await CookieAuthTestHelper.LoginAsAdminAsync(factory);
        using (client)
        {
            CookieAuthTestHelper.UseDefaultCsrfHeader(client, csrf);
            try
            {
                using var response = await client.PostAsJsonAsync(
                    "/api/users",
                    new
                    {
                        fullName = "Audit Created",
                        username,
                        email = $"{username}@example.test",
                        password = "CreatePassword12!",
                        role = "Support"
                    });
                Assert.Equal(HttpStatusCode.Created, response.StatusCode);
                using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                createdId = doc.RootElement.GetProperty("id").GetGuid();

                await using var scope = factory.Services.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var logs = db.UserAuditEvents.Where(e => e.TargetUserId == createdId.Value).ToList();
                var created = Assert.Single(logs);
                Assert.Equal("Created", created.EventType);
                Assert.Equal(adminId, created.ActorUserId);
                Assert.Equal(createdId.Value, created.TargetUserId);
                Assert.Equal("Support", created.AfterRole);
                Assert.Null(created.BeforeRole);
                Assert.Null(created.BeforeIsActive);
                Assert.Null(created.AfterIsActive);
                Assert.True((DateTime.UtcNow - created.CreatedAt).TotalMinutes < 5);
                Assert.Null(created.CorrelationId);
                // never stores password or hash
                Assert.DoesNotContain("CreatePassword12!", created.ToString() ?? string.Empty);
            }
            finally
            {
                if (createdId is Guid id)
                {
                    await IntegrationTestUser.DeleteAsync(factory.Services, id);
                    await CleanupAuditAsync(id);
                }
            }
        }
    }

    [Fact]
    public async Task UpdateUser_role_change_emits_audit_event()
    {
        var token = Guid.NewGuid().ToString("N");
        var username = $"u-audit-r-{token[..12]}";
        Guid? createdId = null;

        var (client, csrf, adminId) = await CookieAuthTestHelper.LoginAsAdminAsync(factory);
        using (client)
        {
            CookieAuthTestHelper.UseDefaultCsrfHeader(client, csrf);
            try
            {
                using var createResponse = await client.PostAsJsonAsync(
                    "/api/users",
                    new
                    {
                        fullName = "Audit Role",
                        username,
                        email = $"{username}@example.test",
                        password = "CreatePassword12!",
                        role = "Support"
                    });
                createResponse.EnsureSuccessStatusCode();
                using var createDoc = JsonDocument.Parse(await createResponse.Content.ReadAsStringAsync());
                createdId = createDoc.RootElement.GetProperty("id").GetGuid();

                // clean the Created audit to isolate RoleChanged
                await CleanupAuditAsync(createdId.Value);

                using var updateResponse = await client.PutAsJsonAsync(
                    $"/api/users/{createdId}",
                    new
                    {
                        fullName = "Audit Role",
                        email = $"{username}@example.test",
                        role = "Admin",
                        isActive = true
                    });
                Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);

                await using var scope = factory.Services.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var logs = db.UserAuditEvents.Where(e => e.TargetUserId == createdId.Value).ToList();
                var roleLog = Assert.Single(logs, l => l.EventType == "RoleChanged");
                Assert.Equal(adminId, roleLog.ActorUserId);
                Assert.Equal("Support", roleLog.BeforeRole);
                Assert.Equal("Admin", roleLog.AfterRole);
                Assert.Null(roleLog.BeforeIsActive);
                Assert.Null(roleLog.AfterIsActive);
            }
            finally
            {
                if (createdId is Guid id)
                {
                    await IntegrationTestUser.DeleteAsync(factory.Services, id);
                    await CleanupAuditAsync(id);
                }
            }
        }
    }

    [Fact]
    public async Task UpdateUser_active_change_emits_audit_event()
    {
        var token = Guid.NewGuid().ToString("N");
        var username = $"u-audit-a-{token[..12]}";
        Guid? createdId = null;

        var (client, csrf, adminId) = await CookieAuthTestHelper.LoginAsAdminAsync(factory);
        using (client)
        {
            CookieAuthTestHelper.UseDefaultCsrfHeader(client, csrf);
            try
            {
                using var createResponse = await client.PostAsJsonAsync(
                    "/api/users",
                    new
                    {
                        fullName = "Audit Active",
                        username,
                        email = $"{username}@example.test",
                        password = "CreatePassword12!",
                        role = "Support"
                    });
                createResponse.EnsureSuccessStatusCode();
                using var createDoc = JsonDocument.Parse(await createResponse.Content.ReadAsStringAsync());
                createdId = createDoc.RootElement.GetProperty("id").GetGuid();

                await CleanupAuditAsync(createdId.Value);

                using var updateResponse = await client.PutAsJsonAsync(
                    $"/api/users/{createdId}",
                    new
                    {
                        fullName = "Audit Active",
                        email = $"{username}@example.test",
                        role = "Support",
                        isActive = false
                    });
                Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);

                await using var scope = factory.Services.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var logs = db.UserAuditEvents.Where(e => e.TargetUserId == createdId.Value).ToList();
                var activeLog = Assert.Single(logs, l => l.EventType == "ActiveChanged");
                Assert.Equal(adminId, activeLog.ActorUserId);
                Assert.Equal(true, activeLog.BeforeIsActive);
                Assert.Equal(false, activeLog.AfterIsActive);
                Assert.Null(activeLog.BeforeRole);
                Assert.Null(activeLog.AfterRole);
            }
            finally
            {
                if (createdId is Guid id)
                {
                    await IntegrationTestUser.DeleteAsync(factory.Services, id);
                    await CleanupAuditAsync(id);
                }
            }
        }
    }

    [Fact]
    public async Task SetPassword_does_not_log_secret()
    {
        var token = Guid.NewGuid().ToString("N");
        var username = $"u-audit-p-{token[..12]}";
        const string newPassword = "RotatedPassw0rd!";
        Guid? createdId = null;

        var (client, csrf, adminId) = await CookieAuthTestHelper.LoginAsAdminAsync(factory);
        using (client)
        {
            CookieAuthTestHelper.UseDefaultCsrfHeader(client, csrf);
            try
            {
                using var createResponse = await client.PostAsJsonAsync(
                    "/api/users",
                    new
                    {
                        fullName = "Audit Password",
                        username,
                        email = $"{username}@example.test",
                        password = "InitialPassw0rd!",
                        role = "Support"
                    });
                createResponse.EnsureSuccessStatusCode();
                using var createDoc = JsonDocument.Parse(await createResponse.Content.ReadAsStringAsync());
                createdId = createDoc.RootElement.GetProperty("id").GetGuid();

                await CleanupAuditAsync(createdId.Value);

                using var pwResponse = await client.PostAsJsonAsync(
                    $"/api/users/{createdId}/password",
                    new { password = newPassword });
                Assert.Equal(HttpStatusCode.NoContent, pwResponse.StatusCode);

                await using var scope = factory.Services.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var logs = db.UserAuditEvents.Where(e => e.TargetUserId == createdId.Value).ToList();
                var pwLog = Assert.Single(logs, l => l.EventType == "PasswordReset");
                Assert.Equal(adminId, pwLog.ActorUserId);
                Assert.Null(pwLog.BeforeRole);
                Assert.Null(pwLog.AfterRole);
                Assert.Null(pwLog.BeforeIsActive);
                Assert.Null(pwLog.AfterIsActive);

                // Audit entity must not contain password fields and must not leak secret anywhere
                var props = typeof(VSHelpDesk.Domain.Entities.UserAuditEvent).GetProperties()
                    .Select(p => p.Name).ToList();
                Assert.DoesNotContain("Password", string.Join(",", props));
                Assert.DoesNotContain("PasswordHash", string.Join(",", props));
                // ensure serialized log does not contain password
                var json = JsonSerializer.Serialize(pwLog);
                Assert.DoesNotContain(newPassword, json);
                Assert.DoesNotContain("InitialPassw0rd!", json);
            }
            finally
            {
                if (createdId is Guid id)
                {
                    await IntegrationTestUser.DeleteAsync(factory.Services, id);
                    await CleanupAuditAsync(id);
                }
            }
        }
    }

    private async Task CleanupAuditAsync(Guid targetUserId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var rows = db.UserAuditEvents.Where(e => e.TargetUserId == targetUserId).ToList();
        if (rows.Count > 0)
        {
            db.UserAuditEvents.RemoveRange(rows);
            await db.SaveChangesAsync();
        }
    }
}
