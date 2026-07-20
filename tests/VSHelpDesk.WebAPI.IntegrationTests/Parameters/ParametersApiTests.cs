using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using VSHelpDesk.Application.Features.Parameters;
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
        // CSRF middleware gates unsafe /api methods before authorization (no cookies → 403).
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetParameters_ReturnsCatalog_WhenAuthenticated()
    {
        var (client, _, _) = await CookieAuthTestHelper.LoginAsSupportAsync(factory);
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
    public async Task PutParameter_UpdatesValue()
    {
        var (client, csrf, _) = await CookieAuthTestHelper.LoginAsSupportAsync(factory);
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
    public async Task PutParameter_InvalidRange_Returns400(string invalidValue)
    {
        var (client, csrf, _) = await CookieAuthTestHelper.LoginAsSupportAsync(factory);
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
    public async Task PutParameter_UnknownKey_Returns404()
    {
        var (client, csrf, _) = await CookieAuthTestHelper.LoginAsSupportAsync(factory);
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
    public async Task PutParameter_NullBody_Returns400()
    {
        var (client, csrf, _) = await CookieAuthTestHelper.LoginAsSupportAsync(factory);
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
}
