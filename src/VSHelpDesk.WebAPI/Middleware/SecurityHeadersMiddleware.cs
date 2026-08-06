namespace VSHelpDesk.WebAPI.Middleware;

/// <summary>
/// Adds hardened security headers to every response: CSP, Permissions-Policy, X-Content-Type-Options, X-Frame-Options, Referrer-Policy.
/// Must run early and use OnStarting so headers are present even on error/404 responses.
/// Mirrors nginx headers (frontend/nginx.conf) for defense-in-depth when API is called directly.
/// </summary>
public sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    public const string CspValue =
        "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; font-src 'self' data:; img-src 'self' data:; connect-src 'self'; object-src 'none'; base-uri 'none'; frame-ancestors 'none'; form-action 'self'";

    public const string PermissionsPolicyValue = "camera=(), microphone=(), geolocation=()";

    public async Task InvokeAsync(HttpContext context)
    {
        context.Response.OnStarting(() =>
        {
            var headers = context.Response.Headers;
            if (!headers.ContainsKey("Content-Security-Policy"))
                headers["Content-Security-Policy"] = CspValue;
            if (!headers.ContainsKey("Permissions-Policy"))
                headers["Permissions-Policy"] = PermissionsPolicyValue;
            if (!headers.ContainsKey("X-Content-Type-Options"))
                headers["X-Content-Type-Options"] = "nosniff";
            if (!headers.ContainsKey("X-Frame-Options"))
                headers["X-Frame-Options"] = "DENY";
            if (!headers.ContainsKey("Referrer-Policy"))
                headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
            return Task.CompletedTask;
        });

        await next(context);
    }
}
