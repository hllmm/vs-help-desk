using Microsoft.Extensions.Options;
using VSHelpDesk.Application;
using VSHelpDesk.Application.Abstractions.Authentication;
using VSHelpDesk.Infrastructure;
using VSHelpDesk.Infrastructure.Persistence.Seed;
using VSHelpDesk.WebAPI.Extensions;
using VSHelpDesk.WebAPI.Filters;
using VSHelpDesk.WebAPI.Options;
using VSHelpDesk.WebAPI.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
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

// React SPA (Vite) — only configured development origins (see Cors:AllowedOrigins).
var corsOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? Array.Empty<string>();
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
                .AllowAnyMethod();
        });
});

var app = builder.Build();

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
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

// Health check for local/docker smoke tests (no auth).
app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "VSHelpDesk.WebAPI" }))
    .AllowAnonymous();

app.Run();

// Integration tests (Hafta 1+) can use WebApplicationFactory with this partial.
public partial class Program;
