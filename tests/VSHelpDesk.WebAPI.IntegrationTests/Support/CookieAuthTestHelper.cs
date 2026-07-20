using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace VSHelpDesk.WebAPI.IntegrationTests.Support;

/// <summary>
/// Cookie-jar helpers for portal auth integration tests (HttpOnly JWT + CSRF cookies).
/// </summary>
public static class CookieAuthTestHelper
{
    public const string AuthCookieName = "vshd.auth";
    public const string CsrfCookieName = "vshd.csrf";
    public const string CsrfHeaderName = "X-CSRF-Token";

    public static HttpClient CreateCookieClient(WebApplicationFactory<Program> factory)
    {
        return factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        });
    }

    public static async Task<HttpResponseMessage> LoginAsync(
        HttpClient client,
        string username,
        string password)
    {
        return await client.PostAsJsonAsync(
            "/api/auth/login",
            new { username, password });
    }

    /// <summary>
    /// Logs in as the configured seed support user with a cookie-aware client.
    /// Auth is carried by the HttpOnly cookie jar; mutating requests need CSRF
    /// via <see cref="AddCsrf"/> or <see cref="UseDefaultCsrfHeader"/>.
    /// </summary>
    public static async Task<(HttpClient Client, string Csrf, Guid UserId)> LoginAsSupportAsync(
        WebApplicationFactory<Program> factory)
    {
        var (client, csrf, userId, _) = await LoginAsSupportFullAsync(factory);
        return (client, csrf, userId);
    }

    /// <summary>
    /// Same as <see cref="LoginAsSupportAsync"/> but also returns the auth JWT cookie value
    /// (for tests that must replay cookies onto a different WebApplicationFactory host).
    /// </summary>
    public static async Task<(HttpClient Client, string Csrf, Guid UserId, string AuthJwt)> LoginAsSupportFullAsync(
        WebApplicationFactory<Program> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);

        var (username, password) = GetSeedCredentials(factory);

        var client = CreateCookieClient(factory);
        HttpResponseMessage loginResponse;
        try
        {
            loginResponse = await LoginAsync(client, username, password);
        }
        catch
        {
            client.Dispose();
            throw;
        }

        using (loginResponse)
        {
            if (!loginResponse.IsSuccessStatusCode)
            {
                client.Dispose();
                var body = await loginResponse.Content.ReadAsStringAsync();
                throw new InvalidOperationException(
                    $"Seed support login failed with {(int)loginResponse.StatusCode}: {body}");
            }

            var setCookies = GetSetCookieHeaders(loginResponse);
            var csrf = GetCookieValue(setCookies, CsrfCookieName);
            var authJwt = GetCookieValue(setCookies, AuthCookieName);
            if (string.IsNullOrWhiteSpace(csrf))
            {
                client.Dispose();
                throw new InvalidOperationException(
                    $"Expected Set-Cookie {CsrfCookieName} after successful login.");
            }

            if (string.IsNullOrWhiteSpace(authJwt))
            {
                client.Dispose();
                throw new InvalidOperationException(
                    $"Expected Set-Cookie {AuthCookieName} after successful login.");
            }

            using var doc = JsonDocument.Parse(await loginResponse.Content.ReadAsStringAsync());
            var userId = doc.RootElement.GetProperty("userId").GetGuid();
            if (userId == Guid.Empty)
            {
                client.Dispose();
                throw new InvalidOperationException("Login body userId was empty.");
            }

            return (client, csrf, userId, authJwt);
        }
    }

    /// <summary>
    /// Captures auth JWT + CSRF from seed login without retaining a cookie jar
    /// (use when attaching cookies manually to another factory's client).
    /// </summary>
    public static async Task<(string AuthJwt, string Csrf, Guid UserId)> CaptureSupportLoginAsync(
        WebApplicationFactory<Program> factory)
    {
        var (client, csrf, userId, authJwt) = await LoginAsSupportFullAsync(factory);
        client.Dispose();
        return (authJwt, csrf, userId);
    }

    public static void AddCsrf(HttpRequestMessage request, string csrf)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Headers.TryAddWithoutValidation(CsrfHeaderName, csrf);
    }

    /// <summary>
    /// Attaches portal auth + CSRF cookies on a request (for hosts without a shared cookie jar).
    /// </summary>
    public static void AddAuthCookies(HttpRequestMessage request, string authJwt, string csrf)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Headers.TryAddWithoutValidation(
            "Cookie",
            $"{AuthCookieName}={authJwt}; {CsrfCookieName}={csrf}");
    }

    /// <summary>
    /// Sets <c>X-CSRF-Token</c> as a default request header (handy for PutAsJsonAsync/PostAsJsonAsync).
    /// </summary>
    public static void UseDefaultCsrfHeader(HttpClient client, string csrf)
    {
        ArgumentNullException.ThrowIfNull(client);
        client.DefaultRequestHeaders.Remove(CsrfHeaderName);
        client.DefaultRequestHeaders.TryAddWithoutValidation(CsrfHeaderName, csrf);
    }

    public static (string Username, string Password) GetSeedCredentials(
        WebApplicationFactory<Program> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        using var scope = factory.Services.CreateScope();
        var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var username = configuration["SeedUser:Username"];
        var password = configuration["SeedUser:Password"];
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            throw new InvalidOperationException(
                "SeedUser:Username and SeedUser:Password must be configured for integration tests.");
        }

        return (username, password);
    }

    public static IReadOnlyList<string> GetSetCookieHeaders(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var values))
        {
            return Array.Empty<string>();
        }

        return values.ToList();
    }

    public static string? FindSetCookie(IReadOnlyList<string> setCookies, string name)
    {
        var prefix = name + "=";
        return setCookies.FirstOrDefault(c =>
            c.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Extracts the cookie value from a Set-Cookie header list (name=value; attrs…).
    /// </summary>
    public static string? GetCookieValue(IReadOnlyList<string> setCookies, string name)
    {
        var header = FindSetCookie(setCookies, name);
        if (header is null)
        {
            return null;
        }

        var prefix = name + "=";
        if (!header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var rest = header[prefix.Length..];
        var end = rest.IndexOf(';');
        return end >= 0 ? rest[..end] : rest;
    }

    public static bool HasCookieAttribute(string setCookieHeader, string attribute)
    {
        // Attributes are "; Attr" or "; Attr=value" (case-insensitive).
        return setCookieHeader.Contains(";" + attribute, StringComparison.OrdinalIgnoreCase)
            || setCookieHeader.Contains("; " + attribute, StringComparison.OrdinalIgnoreCase);
    }

    public static LoginBody? ParseLoginBody(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var userId = root.TryGetProperty("userId", out var userIdEl)
            && userIdEl.TryGetGuid(out var id)
            ? id
            : Guid.Empty;
        var fullName = root.TryGetProperty("fullName", out var fullNameEl)
            ? fullNameEl.GetString() ?? string.Empty
            : string.Empty;
        var username = root.TryGetProperty("username", out var usernameEl)
            ? usernameEl.GetString() ?? string.Empty
            : string.Empty;
        var hasAccessToken = root.TryGetProperty("accessToken", out _);

        return new LoginBody(userId, fullName, username, hasAccessToken);
    }

    public sealed record LoginBody(
        Guid UserId,
        string FullName,
        string Username,
        bool HasAccessToken);
}
