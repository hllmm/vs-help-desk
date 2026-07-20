using System.Security.Cryptography;
using System.Text;
using VSHelpDesk.WebAPI.Authentication;

namespace VSHelpDesk.WebAPI.Middleware;

/// <summary>
/// Double-submit CSRF gate for portal API mutations: <c>X-CSRF-Token</c> must match cookie <c>vshd.csrf</c>.
/// Skips safe methods, login, and jobs API-key routes.
/// </summary>
public sealed class CsrfProtectionMiddleware(RequestDelegate next)
{
    private static readonly HashSet<string> UnsafeMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        "POST",
        "PUT",
        "PATCH",
        "DELETE"
    };

    public async Task InvokeAsync(HttpContext context)
    {
        if (RequiresCsrf(context))
        {
            var cookie = context.Request.Cookies[AuthCookieNames.Csrf];
            var header = context.Request.Headers["X-CSRF-Token"].FirstOrDefault();

            if (string.IsNullOrEmpty(cookie)
                || string.IsNullOrEmpty(header)
                || !CsrfEquals(cookie, header))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsJsonAsync(new
                {
                    status = 403,
                    title = "CSRF validation failed."
                });
                return;
            }
        }

        await next(context);
    }

    private static bool RequiresCsrf(HttpContext context)
    {
        if (!UnsafeMethods.Contains(context.Request.Method))
        {
            return false;
        }

        var path = context.Request.Path.Value ?? string.Empty;
        if (path.Equals("/api/auth/login", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (path.StartsWith("/api/jobs", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!path.StartsWith("/api", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Fixed-time equality; rejects length mismatches without throwing.
    /// </summary>
    private static bool CsrfEquals(string a, string b)
    {
        var left = Encoding.UTF8.GetBytes(a);
        var right = Encoding.UTF8.GetBytes(b);
        return left.Length == right.Length
            && CryptographicOperations.FixedTimeEquals(left, right);
    }
}
