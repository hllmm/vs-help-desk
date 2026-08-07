using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VSHelpDesk.Infrastructure.Persistence;
using VSHelpDesk.WebAPI.IntegrationTests.Support;

namespace VSHelpDesk.WebAPI.IntegrationTests.Tickets;

public sealed class PortalTicketIdempotencyApiTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly WebApplicationFactory<Program> factory;

    public PortalTicketIdempotencyApiTests(CustomWebApplicationFactory factory)
    {
        this.factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("environment", "Development");
            builder.UseEnvironment("Development");
        });
    }

    [Fact]
    public async Task CreatePortalTicket_RequiresIdempotencyKey()
    {
        var (client, csrf, _) = await CookieAuthTestHelper.LoginAsSupportAsync(factory);
        using (client)
        using (var response = await SendCreateAsync(client, csrf, key: null, CreateRequest("missing-key")))
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
    }

    [Fact]
    public async Task CreatePortalTicket_RejectsInvalidIdempotencyKey()
    {
        var (client, csrf, _) = await CookieAuthTestHelper.LoginAsSupportAsync(factory);
        using (client)
        using (var response = await SendCreateAsync(client, csrf, "not-a-uuid", CreateRequest("invalid-key")))
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
    }

    [Fact]
    public async Task CreatePortalTicket_ReplaysForSameUserAndPayload()
    {
        var request = CreateRequest($"replay-{Guid.NewGuid():N}");
        var key = Guid.NewGuid().ToString();
        var createdTicketIds = new List<Guid>();
        var (client, csrf, _) = await CookieAuthTestHelper.LoginAsSupportAsync(factory);

        try
        {
            using var first = await SendCreateAsync(client, csrf, key, request);
            using var replay = await SendCreateAsync(client, csrf, key, request);

            Assert.Equal(HttpStatusCode.Created, first.StatusCode);
            Assert.True(replay.IsSuccessStatusCode, await replay.Content.ReadAsStringAsync());

            var firstTicketId = await ReadTicketIdAsync(first);
            var replayTicketId = await ReadTicketIdAsync(replay);
            createdTicketIds.Add(firstTicketId);
            createdTicketIds.Add(replayTicketId);

            Assert.Equal(firstTicketId, replayTicketId);
            Assert.Equal(1, await CountTicketsAsync(request.Subject));
        }
        finally
        {
            await DeleteTicketsAsync(createdTicketIds);
            client.Dispose();
        }
    }

    [Fact]
    public async Task CreatePortalTicket_RejectsSameUserAndKeyWithDifferentPayload()
    {
        var firstRequest = CreateRequest($"conflict-{Guid.NewGuid():N}");
        var changedRequest = firstRequest with { Content = firstRequest.Content + " changed" };
        var key = Guid.NewGuid().ToString();
        var createdTicketIds = new List<Guid>();
        var (client, csrf, _) = await CookieAuthTestHelper.LoginAsSupportAsync(factory);

        try
        {
            using var first = await SendCreateAsync(client, csrf, key, firstRequest);
            using var conflict = await SendCreateAsync(client, csrf, key, changedRequest);

            Assert.Equal(HttpStatusCode.Created, first.StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);

            createdTicketIds.Add(await ReadTicketIdAsync(first));
            Assert.Equal(1, await CountTicketsAsync(firstRequest.Subject));
        }
        finally
        {
            await DeleteTicketsAsync(createdTicketIds);
            client.Dispose();
        }
    }

    [Fact]
    public async Task CreatePortalTicket_AllowsSameKeyForDifferentUsers()
    {
        var key = Guid.NewGuid().ToString();
        var supportRequest = CreateRequest($"support-key-{Guid.NewGuid():N}");
        var adminRequest = CreateRequest($"admin-key-{Guid.NewGuid():N}");
        var createdTicketIds = new List<Guid>();
        var (supportClient, supportCsrf, _) = await CookieAuthTestHelper.LoginAsSupportAsync(factory);
        var (adminClient, adminCsrf, _) = await CookieAuthTestHelper.LoginAsAdminAsync(factory);

        try
        {
            using var supportResponse = await SendCreateAsync(
                supportClient,
                supportCsrf,
                key,
                supportRequest);
            using var adminResponse = await SendCreateAsync(
                adminClient,
                adminCsrf,
                key,
                adminRequest);

            Assert.Equal(HttpStatusCode.Created, supportResponse.StatusCode);
            Assert.Equal(HttpStatusCode.Created, adminResponse.StatusCode);

            createdTicketIds.Add(await ReadTicketIdAsync(supportResponse));
            createdTicketIds.Add(await ReadTicketIdAsync(adminResponse));
            Assert.NotEqual(createdTicketIds[0], createdTicketIds[1]);
        }
        finally
        {
            await DeleteTicketsAsync(createdTicketIds);
            supportClient.Dispose();
            adminClient.Dispose();
        }
    }

    [Fact]
    public async Task CreatePortalTicket_ParallelIdenticalRequestsCreateExactlyOneTicket()
    {
        var request = CreateRequest($"parallel-{Guid.NewGuid():N}");
        var key = Guid.NewGuid().ToString();
        var (client, csrf, _) = await CookieAuthTestHelper.LoginAsSupportAsync(factory);
        var responses = Array.Empty<HttpResponseMessage>();
        var createdTicketIds = new List<Guid>();

        try
        {
            responses = await Task.WhenAll(
                Enumerable.Range(0, 8)
                    .Select(_ => SendCreateAsync(client, csrf, key, request)));

            Assert.All(
                responses,
                response => Assert.True(
                    response.IsSuccessStatusCode,
                    $"Expected a successful replay response, got {(int)response.StatusCode}: "
                    + response.Content.ReadAsStringAsync().GetAwaiter().GetResult()));

            foreach (var response in responses)
            {
                createdTicketIds.Add(await ReadTicketIdAsync(response));
            }

            Assert.Single(createdTicketIds.Distinct());
            Assert.Equal(1, await CountTicketsAsync(request.Subject));
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }

            await DeleteTicketsAsync(createdTicketIds);
            client.Dispose();
        }
    }

    private static async Task<HttpResponseMessage> SendCreateAsync(
        HttpClient client,
        string csrf,
        string? key,
        PortalTicketRequest request)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, "/api/tickets")
        {
            Content = JsonContent.Create(request)
        };
        CookieAuthTestHelper.AddCsrf(message, csrf);
        if (key is not null)
        {
            message.Headers.TryAddWithoutValidation("Idempotency-Key", key);
        }

        return await client.SendAsync(message);
    }

    private static PortalTicketRequest CreateRequest(string token) =>
        new(
            Subject: $"Portal idempotency {token}",
            CustomerName: "Portal Customer",
            CustomerEmail: $"portal-{token}@example.test",
            Content: $"Portal content {token}");

    private static async Task<Guid> ReadTicketIdAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("ticketId").GetGuid();
    }

    private async Task<int> CountTicketsAsync(string subject)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.Tickets.CountAsync(ticket => ticket.Subject == subject);
    }

    private async Task DeleteTicketsAsync(IEnumerable<Guid> ticketIds)
    {
        var ids = ticketIds.Where(id => id != Guid.Empty).Distinct().ToArray();
        if (ids.Length == 0)
        {
            return;
        }

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        try
        {
            await db.Tickets.Where(ticket => ids.Contains(ticket.Id)).ExecuteDeleteAsync();
        }
        catch (InvalidOperationException)
        {
            var tickets = await db.Tickets.Where(ticket => ids.Contains(ticket.Id)).ToListAsync();
            db.Tickets.RemoveRange(tickets);
            await db.SaveChangesAsync();
        }
    }

    private sealed record PortalTicketRequest(
        string Subject,
        string CustomerName,
        string CustomerEmail,
        string Content);
}
