using System.Globalization;
using System.Net;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VSHelpDesk.Application;
using VSHelpDesk.Application.Abstractions.Authentication;
using VSHelpDesk.Application.Common.Localization;
using VSHelpDesk.Infrastructure;
using VSHelpDesk.Infrastructure.Persistence;
using VSHelpDesk.Infrastructure.Persistence.Seed;
using VSHelpDesk.WebAPI.Extensions;
using VSHelpDesk.WebAPI.Filters;
using VSHelpDesk.WebAPI.Middleware;
using VSHelpDesk.WebAPI.Options;
using VSHelpDesk.WebAPI.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers()
    .ConfigureApiBehaviorOptions(options =>
    {
        options.InvalidModelStateResponseFactory = context =>
        {
            var messages = context.HttpContext.RequestServices
                .GetRequiredService<IMessageProvider>();

            var isAttachmentUpload =
                context.ActionDescriptor.RouteValues.TryGetValue("controller", out var controller) &&
                context.ActionDescriptor.RouteValues.TryGetValue("action", out var action) &&
                string.Equals(controller, "Attachments", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(action, "Upload", StringComparison.OrdinalIgnoreCase);

            if (isAttachmentUpload)
            {
                return new BadRequestObjectResult(new
                {
                    message = messages.Get(MessageKeys.Attachments.FileRequired)
                });
            }

            var problemDetailsFactory = context.HttpContext.RequestServices
                .GetRequiredService<ProblemDetailsFactory>();
            var problemDetails = problemDetailsFactory.CreateValidationProblemDetails(
                context.HttpContext,
                context.ModelState,
                statusCode: StatusCodes.Status400BadRequest);

            return new BadRequestObjectResult(problemDetails);
        };
    });
builder.Services.AddOpenApi();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUserService, HttpCurrentUserService>();

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddJwtBearerAuthentication(builder.Configuration);

builder.Services.AddSingleton<IValidateOptions<JobsOptions>, JobsOptionsValidator>();
builder.Services.AddOptions<JobsOptions>()
    .Bind(builder.Configuration.GetSection(JobsOptions.SectionName))
    .ValidateOnStart();
builder.Services.AddScoped<JobsApiKeyAuthorizationFilter>();

builder.Services.AddHealthChecks()
    .AddDbContextCheck<ApplicationDbContext>("database");

// Login abuse protection (single-instance memory partitioner).
// Partition key uses httpContext.Connection.RemoteIpAddress AFTER ForwardedHeaders sanitization
// (UseForwardedHeaders runs before UseRateLimiter). ForwardLimit=2 models edge+web nginx (see ForwardedHeadersOptions).
// X-Login-Username is normalized (Trim+LowerInvariant) and only partitions — not trusted for auth.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, token) =>
    {
        var messages = context.HttpContext.RequestServices.GetRequiredService<IMessageProvider>();
        context.HttpContext.Response.ContentType = "application/json";
        await context.HttpContext.Response.WriteAsJsonAsync(
            new { message = messages.Get(MessageKeys.Http.RateLimitExceeded) },
            token);
    };

    options.AddPolicy(
        "auth-login",
        httpContext =>
        {
            var ip = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            var usernameHeader = httpContext.Request.Headers["X-Login-Username"].FirstOrDefault()?.Trim().ToLowerInvariant();
            var partitionKey = string.IsNullOrWhiteSpace(usernameHeader)
                ? $"login:{ip}"
                : $"login:{ip}:{usernameHeader}";

            // IP & Username partition; development uses higher limit for integration testing suites.
            var permitLimit = builder.Environment.IsDevelopment() ? 1_000 : 10;
            return RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: partitionKey,
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = permitLimit,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                    AutoReplenishment = true
                });
        });
});

// React SPA (Vite) — only configured development origins (see Cors:AllowedOrigins).
var corsOrigins = (builder.Configuration
        .GetSection("Cors:AllowedOrigins")
        .Get<string[]>() ?? Array.Empty<string>())
    .Where(origin => !string.IsNullOrWhiteSpace(origin))
    .Select(origin => origin.Trim())
    .Distinct(StringComparer.OrdinalIgnoreCase)
    .ToArray();
builder.Services.AddCors(options =>
{
    options.AddPolicy(
        "Portal",
        policy =>
        {
            if (corsOrigins.Length == 0)
            {
                // Fail closed when misconfigured: no browser origins allowed.
                policy.SetIsOriginAllowed(_ => false);
                return;
            }

            policy
                .WithOrigins(corsOrigins)
                .AllowAnyHeader()
                .AllowAnyMethod()
                .AllowCredentials();
        });
});

// Trust reverse-proxy headers — 2-hop chain: edge/Ingress -> web nginx -> API.
var trustedNetworks = builder.Configuration
    .GetSection("ForwardedHeaders:TrustedNetworks")
    .Get<string[]>() ?? ["127.0.0.1/32", "::1/128"];
var forwardLimit = builder.Configuration.GetValue<int?>("ForwardedHeaders:ForwardLimit") ?? 2;

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders =
        Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor
        | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto;
    options.ForwardLimit = forwardLimit;
    options.RequireHeaderSymmetry = false;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();

    foreach (var cidr in trustedNetworks)
    {
        if (!string.IsNullOrWhiteSpace(cidr) && IPNetwork.TryParse(cidr, out var network))
        {
            options.KnownIPNetworks.Add(network);
        }
    }
});

var app = builder.Build();

app.UseForwardedHeaders();

var supportedCultures = new[] { new CultureInfo("tr-TR"), new CultureInfo("en-US") };
app.UseRequestLocalization(new RequestLocalizationOptions
{
    DefaultRequestCulture = new RequestCulture("tr-TR"),
    SupportedCultures = supportedCultures,
    SupportedUICultures = supportedCultures
});

if (app.Environment.IsDevelopment())
{
    await app.Services.SeedDevelopmentDataAsync();
    app.MapOpenApi().AllowAnonymous();
}

// Local SPA talks HTTP → API; avoid forcing HTTPS redirects that break CORS preflight.
if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.UseExceptionHandling();
app.UseCors("Portal");
app.UseRateLimiter();
app.UseAuthentication();
app.UseMiddleware<CsrfProtectionMiddleware>();
app.UseAuthorization();

app.MapControllers();

app.MapHealthChecks("/health/live", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = _ => false
}).AllowAnonymous();

app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = check => check.Name == "database"
}).AllowAnonymous();

// Backward-compatible liveness alias used by older smoke scripts.
app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "VSHelpDesk.WebAPI" }))
    .AllowAnonymous();

if (app.Environment.IsDevelopment())
{
    // Diagnostic probes for ForwardedHeaders integration tests (SEC-005).
    // Expose sanitized RemoteIpAddress and Request.Scheme after UseForwardedHeaders.
    app.MapGet("/__test/remote-ip", (HttpContext ctx) => Results.Ok(new
    {
        ip = ctx.Connection.RemoteIpAddress?.ToString(),
        scheme = ctx.Request.Scheme
    })).AllowAnonymous();

    app.MapGet("/__test/rate-limit-key", (HttpContext ctx) =>
    {
        var ip = ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var usernameHeader = ctx.Request.Headers["X-Login-Username"].FirstOrDefault()?.Trim().ToLowerInvariant();
        var key = string.IsNullOrWhiteSpace(usernameHeader)
            ? $"login:{ip}"
            : $"login:{ip}:{usernameHeader}";
        return Results.Ok(new { partitionKey = key, ip, scheme = ctx.Request.Scheme });
    }).AllowAnonymous();
}

app.Run();

// Integration tests (Hafta 1+) can use WebApplicationFactory with this partial.
public partial class Program;
