using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace VSHelpDesk.WebAPI.IntegrationTests.Support;

/// <summary>
/// Cookie-jar helpers for portal auth integration tests (HttpOnly JWT + CSRF cookies).
/// </summary>
public static class CookieAuthTestHelper
{
    public const string AuthCookieName = "vshd.auth";
    public const string CsrfCookieName = "vshd.csrf";

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
