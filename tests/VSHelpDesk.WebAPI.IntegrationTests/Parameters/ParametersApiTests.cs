using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VSHelpDesk.Application.Features.Parameters;
using VSHelpDesk.Infrastructure.Persistence;
using VSHelpDesk.WebAPI.IntegrationTests.Support;

namespace VSHelpDesk.WebAPI.IntegrationTests.Parameters;

public sealed class ParametersApiTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly WebApplicationFactory<Program> factory;

    public ParametersApiTests(CustomWebApplicationFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task GetParameters_Unauthorized_WithoutToken()
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/api/parameters");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PutParameter_Unauthenticated_WithoutCookies_IsRejected()
    {
        using var client = factory.CreateClient();
        using var response = await client.PutAsJsonAsync(
            $"/api/parameters/{ApplicationParameterCatalog.AutoResolveInactiveDaysKey}",
            new { value = "5" });
        // No vshd.auth → CSRF skipped; [Authorize] returns 401.
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetParameters_Support_Returns403()
    {
        var (client, _, _) = await CookieAuthTestHelper.LoginAsSupportAsync(factory);
        using (client)
        {
            using var response = await client.GetAsync("/api/parameters");
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
    }

    [Fact]
    public async Task PutParameter_Support_Returns403()
    {
        var (client, csrf, _) = await CookieAuthTestHelper.LoginAsSupportAsync(factory);
        using (client)
        {
            CookieAuthTestHelper.UseDefaultCsrfHeader(client, csrf);
            using var response = await client.PutAsJsonAsync(
                $"/api/parameters/{ApplicationParameterCatalog.AutoResolveInactiveDaysKey}",
                new { value = "5" });
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
    }

    [Fact]
    public async Task GetParameters_Admin_Returns200Catalog()
    {
        var (client, _, _) = await CookieAuthTestHelper.LoginAsAdminAsync(factory);
        using (client)
        {
            using var response = await client.GetAsync("/api/parameters");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.NotEqual(HttpStatusCode.NotImplemented, response.StatusCode);

            var json = await response.Content.ReadAsStringAsync();
            Assert.DoesNotContain("501", json);
            Assert.DoesNotContain("NotImplemented", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Bonus", json, StringComparison.OrdinalIgnoreCase);

            using var doc = JsonDocument.Parse(json);
            Assert.Equal(JsonValueKind.Array, doc.RootElement.ValueKind);

            var inactiveDays = doc.RootElement.EnumerateArray()
                .FirstOrDefault(element =>
                    element.TryGetProperty("key", out var key)
                    && key.GetString() == ApplicationParameterCatalog.AutoResolveInactiveDaysKey);

            Assert.NotEqual(default, inactiveDays);
            Assert.True(inactiveDays.TryGetProperty("value", out var value));
            Assert.False(string.IsNullOrWhiteSpace(value.GetString()));
            Assert.True(inactiveDays.TryGetProperty("description", out _));
            Assert.True(inactiveDays.TryGetProperty("updatedAt", out _));
        }
    }

    [Fact]
    public async Task PutParameter_Admin_UpdatesValue()
    {
        var (client, csrf, _) = await CookieAuthTestHelper.LoginAsAdminAsync(factory);
        using (client)
        {
            CookieAuthTestHelper.UseDefaultCsrfHeader(client, csrf);

            // Shared PostgreSQL: capture and restore so other suites keep default 3-day cutoff.
            using var beforeResponse = await client.GetAsync("/api/parameters");
            beforeResponse.EnsureSuccessStatusCode();
            using var beforeDoc = JsonDocument.Parse(await beforeResponse.Content.ReadAsStringAsync());
            var previousValue = beforeDoc.RootElement.EnumerateArray()
                .Single(element =>
                    element.GetProperty("key").GetString()
                    == ApplicationParameterCatalog.AutoResolveInactiveDaysKey)
                .GetProperty("value")
                .GetString();
            Assert.False(string.IsNullOrWhiteSpace(previousValue));

            // Use a distinct in-range value so the assertion is not a false positive against the default.
            const string updatedValue = "7";
            try
            {
                using var putResponse = await client.PutAsJsonAsync(
                    $"/api/parameters/{ApplicationParameterCatalog.AutoResolveInactiveDaysKey}",
                    new { value = updatedValue });

                Assert.Equal(HttpStatusCode.OK, putResponse.StatusCode);
                Assert.NotEqual(HttpStatusCode.NotImplemented, putResponse.StatusCode);

                using var putDoc = JsonDocument.Parse(await putResponse.Content.ReadAsStringAsync());
                Assert.Equal(
                    ApplicationParameterCatalog.AutoResolveInactiveDaysKey,
                    putDoc.RootElement.GetProperty("key").GetString());
                Assert.Equal(updatedValue, putDoc.RootElement.GetProperty("value").GetString());

                using var getResponse = await client.GetAsync("/api/parameters");
                getResponse.EnsureSuccessStatusCode();
                using var getDoc = JsonDocument.Parse(await getResponse.Content.ReadAsStringAsync());
                var inactiveDays = getDoc.RootElement.EnumerateArray()
                    .Single(element =>
                        element.GetProperty("key").GetString()
                        == ApplicationParameterCatalog.AutoResolveInactiveDaysKey);
                Assert.Equal(updatedValue, inactiveDays.GetProperty("value").GetString());
            }
            finally
            {
                using var restoreResponse = await client.PutAsJsonAsync(
                    $"/api/parameters/{ApplicationParameterCatalog.AutoResolveInactiveDaysKey}",
                    new { value = previousValue });
                restoreResponse.EnsureSuccessStatusCode();
            }
        }
    }

    [Theory]
    [InlineData("0")]
    [InlineData("99")]
    [InlineData("")]
    public async Task PutParameter_Admin_InvalidRange_Returns400(string invalidValue)
    {
        var (client, csrf, _) = await CookieAuthTestHelper.LoginAsAdminAsync(factory);
        using (client)
        {
            CookieAuthTestHelper.UseDefaultCsrfHeader(client, csrf);

            using var response = await client.PutAsJsonAsync(
                $"/api/parameters/{ApplicationParameterCatalog.AutoResolveInactiveDaysKey}",
                new { value = invalidValue });

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.NotEqual(HttpStatusCode.NotImplemented, response.StatusCode);

            var body = await response.Content.ReadAsStringAsync();
            Assert.DoesNotContain("501", body);
            Assert.DoesNotContain("Exception", body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("StackTrace", body, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task PutParameter_Admin_UnknownKey_Returns404()
    {
        var (client, csrf, _) = await CookieAuthTestHelper.LoginAsAdminAsync(factory);
        using (client)
        {
            CookieAuthTestHelper.UseDefaultCsrfHeader(client, csrf);

            using var response = await client.PutAsJsonAsync(
                "/api/parameters/nope.Nope",
                new { value = "1" });

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.NotEqual(HttpStatusCode.NotImplemented, response.StatusCode);

            var body = await response.Content.ReadAsStringAsync();
            Assert.DoesNotContain("NotFoundException", body, StringComparison.Ordinal);
            Assert.DoesNotContain("Exception", body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("StackTrace", body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("VSHelpDesk.", body, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task PutParameter_Admin_NullBody_Returns400()
    {
        var (client, csrf, _) = await CookieAuthTestHelper.LoginAsAdminAsync(factory);
        using (client)
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Put,
                $"/api/parameters/{ApplicationParameterCatalog.AutoResolveInactiveDaysKey}")
            {
                Content = new StringContent("null", Encoding.UTF8, "application/json")
            };
            CookieAuthTestHelper.AddCsrf(request, csrf);

            using var response = await client.SendAsync(request);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.NotEqual(HttpStatusCode.NotImplemented, response.StatusCode);
        }
    }

    [Fact]
    public async Task GetParameterAudit_Unauthorized_WithoutToken()
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/api/parameters/audit");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetParameterAudit_Support_Returns403()
    {
        var (client, _, _) = await CookieAuthTestHelper.LoginAsSupportAsync(factory);
        using (client)
        {
            using var response = await client.GetAsync("/api/parameters/audit");
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
    }

    [Fact]
    public async Task GetParameterAudit_Admin_ReturnsRowsWithUsernameAfterPut()
    {
        var (client, csrf, _) = await CookieAuthTestHelper.LoginAsAdminAsync(factory);
        using (client)
        {
            CookieAuthTestHelper.UseDefaultCsrfHeader(client, csrf);

            using var beforeResponse = await client.GetAsync("/api/parameters");
            beforeResponse.EnsureSuccessStatusCode();
            using var beforeDoc = JsonDocument.Parse(await beforeResponse.Content.ReadAsStringAsync());
            var previousValue = beforeDoc.RootElement.EnumerateArray()
                .Single(element =>
                    element.GetProperty("key").GetString()
                    == ApplicationParameterCatalog.AutoResolveInactiveDaysKey)
                .GetProperty("value")
                .GetString();
            Assert.False(string.IsNullOrWhiteSpace(previousValue));

            var updatedValue = previousValue == "6" ? "5" : "6";
            try
            {
                using var putResponse = await client.PutAsJsonAsync(
                    $"/api/parameters/{ApplicationParameterCatalog.AutoResolveInactiveDaysKey}",
                    new { value = updatedValue });
                Assert.Equal(HttpStatusCode.OK, putResponse.StatusCode);

                using var auditResponse = await client.GetAsync(
                    $"/api/parameters/audit?take=20&key={Uri.EscapeDataString(ApplicationParameterCatalog.AutoResolveInactiveDaysKey)}");
                Assert.Equal(HttpStatusCode.OK, auditResponse.StatusCode);

                using var auditDoc = JsonDocument.Parse(await auditResponse.Content.ReadAsStringAsync());
                Assert.Equal(JsonValueKind.Array, auditDoc.RootElement.ValueKind);

                var match = auditDoc.RootElement.EnumerateArray()
                    .FirstOrDefault(element =>
                        element.GetProperty("newValue").GetString() == updatedValue
                        && element.GetProperty("oldValue").GetString() == previousValue);

                Assert.NotEqual(default, match);
                Assert.Equal(
                    ApplicationParameterCatalog.AutoResolveInactiveDaysKey,
                    match.GetProperty("parameterKey").GetString());
                Assert.Equal("admin", match.GetProperty("changedByUsername").GetString());
                Assert.True(match.TryGetProperty("changedByUserId", out _));
                Assert.True(match.TryGetProperty("changedAt", out _));
            }
            finally
            {
                using var restoreResponse = await client.PutAsJsonAsync(
                    $"/api/parameters/{ApplicationParameterCatalog.AutoResolveInactiveDaysKey}",
                    new { value = previousValue });
                restoreResponse.EnsureSuccessStatusCode();
            }
        }
    }

    [Fact]
    public async Task PutParameter_Admin_WritesAuditRowWithOldNewAndActor()
    {
        var (client, csrf, adminId) = await CookieAuthTestHelper.LoginAsAdminAsync(factory);
        using (client)
        {
            CookieAuthTestHelper.UseDefaultCsrfHeader(client, csrf);

            using var beforeResponse = await client.GetAsync("/api/parameters");
            beforeResponse.EnsureSuccessStatusCode();
            using var beforeDoc = JsonDocument.Parse(await beforeResponse.Content.ReadAsStringAsync());
            var previousValue = beforeDoc.RootElement.EnumerateArray()
                .Single(element =>
                    element.GetProperty("key").GetString()
                    == ApplicationParameterCatalog.AutoResolveInactiveDaysKey)
                .GetProperty("value")
                .GetString();
            Assert.False(string.IsNullOrWhiteSpace(previousValue));

            // Distinct in-range value so old ≠ new even if prior suite left a non-default.
            var updatedValue = previousValue == "8" ? "9" : "8";
            try
            {
                using var putResponse = await client.PutAsJsonAsync(
                    $"/api/parameters/{ApplicationParameterCatalog.AutoResolveInactiveDaysKey}",
                    new { value = updatedValue });
                Assert.Equal(HttpStatusCode.OK, putResponse.StatusCode);

                await using var scope = factory.Services.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var log = await db.ParameterChangeLogs
                    .Where(row =>
                        row.ParameterKey == ApplicationParameterCatalog.AutoResolveInactiveDaysKey
                        && row.NewValue == updatedValue
                        && row.ChangedByUserId == adminId)
                    .OrderByDescending(row => row.ChangedAt)
                    .FirstOrDefaultAsync();

                Assert.NotNull(log);
                Assert.Equal(previousValue, log.OldValue);
                Assert.Equal(updatedValue, log.NewValue);
                Assert.Equal(adminId, log.ChangedByUserId);
            }
            finally
            {
                using var restoreResponse = await client.PutAsJsonAsync(
                    $"/api/parameters/{ApplicationParameterCatalog.AutoResolveInactiveDaysKey}",
                    new { value = previousValue });
                restoreResponse.EnsureSuccessStatusCode();
            }
        }
    }
}

