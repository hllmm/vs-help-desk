using VSHelpDesk.Application;
using VSHelpDesk.Infrastructure;
using VSHelpDesk.Infrastructure.Persistence.Seed;
using VSHelpDesk.WebAPI.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddJwtBearerAuthentication(builder.Configuration);

// Hafta 3: CORS for React SPA (see Cors:AllowedOrigins in appsettings)

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    await app.Services.SeedDevelopmentDataAsync();
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.UseExceptionHandling();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

// Health check for local/docker smoke tests
app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "VSHelpDesk.WebAPI" }));

app.Run();

// Integration tests (Hafta 1+) can use WebApplicationFactory with this partial.
public partial class Program;
