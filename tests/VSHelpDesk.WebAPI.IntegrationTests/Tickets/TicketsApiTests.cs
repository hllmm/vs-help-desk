using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using VSHelpDesk.Domain.Entities;
using VSHelpDesk.Infrastructure.Persistence;

namespace VSHelpDesk.WebAPI.IntegrationTests.Tickets;

public sealed class TicketsApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> factory;

    public TicketsApiTests(WebApplicationFactory<Program> factory)
    {
        this.factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("environment", "Development");
            builder.UseEnvironment("Development");
        });
    }

    [Fact]
    public async Task UC003_GetTickets_WithoutToken_Returns401()
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/api/tickets");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task UC003_GetTickets_WithBearer_Returns200Array()
    {
        var token = await LoginAsync();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var stamp = DateTime.UtcNow;
            db.Add(Ticket.Create(
                $"VS-T{stamp:HHmmss}",
                "List seed",
                "Seed Customer",
                "seed@example.test",
                stamp));
            await db.SaveChangesAsync();
        }

        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/tickets");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(JsonValueKind.Array, doc.RootElement.ValueKind);
        Assert.True(doc.RootElement.GetArrayLength() >= 1);
    }

    [Fact]
    public async Task UC004_GetTicket_UnknownId_Returns404()
    {
        var token = await LoginAsync();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/tickets/{Guid.NewGuid()}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UC004_GetTicket_Existing_ReturnsMessagesChronological()
    {
        var token = await LoginAsync();
        Guid ticketId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var stamp = DateTime.UtcNow;
            var ticket = Ticket.Create(
                $"VS-D{stamp:HHmmss}",
                "Detail subject",
                "Ada",
                "ada@example.test",
                stamp);
            ticketId = ticket.Id;
            db.Add(ticket);
            db.Add(new TicketMessage(
                ticket.Id,
                Domain.Enums.MessageSenderType.Customer,
                "First",
                createdAtUtc: stamp.AddMinutes(1)));
            db.Add(new TicketMessage(
                ticket.Id,
                Domain.Enums.MessageSenderType.Support,
                "Second",
                createdAtUtc: stamp.AddMinutes(2)));
            await db.SaveChangesAsync();
        }

        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/tickets/{ticketId}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("Detail subject", doc.RootElement.GetProperty("subject").GetString());
        var messages = doc.RootElement.GetProperty("messages");
        Assert.Equal(2, messages.GetArrayLength());
        Assert.Equal("First", messages[0].GetProperty("content").GetString());
        Assert.Equal("Second", messages[1].GetProperty("content").GetString());
    }

    private async Task<string> LoginAsync()
    {
        using var scope = factory.Services.CreateScope();
        var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var username = configuration["SeedUser:Username"];
        var password = configuration["SeedUser:Password"];
        Assert.False(string.IsNullOrWhiteSpace(username));
        Assert.False(string.IsNullOrWhiteSpace(password));

        using var client = factory.CreateClient();
        using var loginResponse = await client.PostAsJsonAsync(
            "/api/auth/login",
            new { username, password });
        loginResponse.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await loginResponse.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("accessToken").GetString()!;
    }
}
