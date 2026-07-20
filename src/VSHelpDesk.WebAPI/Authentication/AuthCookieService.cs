using System.Security.Cryptography;
using Microsoft.AspNetCore.WebUtilities;
using VSHelpDesk.Infrastructure.Authentication;

namespace VSHelpDesk.WebAPI.Authentication;

/// <summary>
/// Sets and clears portal auth cookies (<c>vshd.auth</c> HttpOnly JWT, <c>vshd.csrf</c> readable).
/// </summary>
public static class AuthCookieService
{
    public static string CreateCsrfToken() =>
        Base64UrlTextEncoder.Encode(RandomNumberGenerator.GetBytes(32));

    public static void AppendAuthCookies(
        HttpResponse response,
        string jwt,
        string csrfToken,
        AuthOptions authOptions,
        bool isDevelopment)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentException.ThrowIfNullOrWhiteSpace(jwt);
        ArgumentException.ThrowIfNullOrWhiteSpace(csrfToken);
        ArgumentNullException.ThrowIfNull(authOptions);

        var maxAge = TimeSpan.FromMinutes(authOptions.ExpirationMinutes);
        var secure = !isDevelopment;

        response.Cookies.Append(
            AuthCookieNames.Auth,
            jwt,
            new CookieOptions
            {
                HttpOnly = true,
                Secure = secure,
                SameSite = SameSiteMode.Lax,
                Path = "/",
                MaxAge = maxAge,
                IsEssential = true
            });

        response.Cookies.Append(
            AuthCookieNames.Csrf,
            csrfToken,
            new CookieOptions
            {
                HttpOnly = false,
                Secure = secure,
                SameSite = SameSiteMode.Lax,
                Path = "/",
                MaxAge = maxAge,
                IsEssential = true
            });
    }

    public static void ClearAuthCookies(HttpResponse response, bool isDevelopment)
    {
        ArgumentNullException.ThrowIfNull(response);

        var secure = !isDevelopment;

        response.Cookies.Delete(
            AuthCookieNames.Auth,
            new CookieOptions
            {
                HttpOnly = true,
                Secure = secure,
                SameSite = SameSiteMode.Lax,
                Path = "/"
            });
        // Ensure browsers that ignore Delete still drop the cookie.
        response.Cookies.Append(
            AuthCookieNames.Auth,
            string.Empty,
            new CookieOptions
            {
                HttpOnly = true,
                Secure = secure,
                SameSite = SameSiteMode.Lax,
                Path = "/",
                MaxAge = TimeSpan.Zero,
                Expires = DateTimeOffset.UnixEpoch,
                IsEssential = true
            });

        response.Cookies.Delete(
            AuthCookieNames.Csrf,
            new CookieOptions
            {
                HttpOnly = false,
                Secure = secure,
                SameSite = SameSiteMode.Lax,
                Path = "/"
            });
        response.Cookies.Append(
            AuthCookieNames.Csrf,
            string.Empty,
            new CookieOptions
            {
                HttpOnly = false,
                Secure = secure,
                SameSite = SameSiteMode.Lax,
                Path = "/",
                MaxAge = TimeSpan.Zero,
                Expires = DateTimeOffset.UnixEpoch,
                IsEssential = true
            });
    }
}
