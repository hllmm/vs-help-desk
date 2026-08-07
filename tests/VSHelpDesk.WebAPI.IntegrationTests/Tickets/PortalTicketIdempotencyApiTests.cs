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
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal(
                "portal-idempotency-key-required",
                body.RootElement.GetProperty("code").GetString());
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
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal(
                "portal-idempotency-key-invalid",
                body.RootElement.GetProperty("code").GetString());
        }
    }

    [Fact]
    public async Task CreatePortalTicket_RejectsOversizedTicketFieldsBeforePersistence()
    {
        var request = CreateRequest($"oversized-{Guid.NewGuid():N}") with
        {
            Subject = new string('s', 501)
        };
        var (client, csrf, _) = await CookieAuthTestHelper.LoginAsSupportAsync(factory);

        try
        {
            using var response = await SendCreateAsync(client, csrf, Guid.NewGuid().ToString(), request);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal(0, await CountTicketsAsync(request.Subject));
        }
        finally
        {
            client.Dispose();
        }
    }

    [Theory]
    [InlineData("not-an-email")]
    [InlineData("Display Name <customer@example.test>")]
    public async Task CreatePortalTicket_RejectsInvalidCustomerEmail(string email)
    {
        var request = CreateRequest($"invalid-email-{Guid.NewGuid():N}") with
        {
            CustomerEmail = email
        };
        var (client, csrf, _) = await CookieAuthTestHelper.LoginAsSupportAsync(factory);

        using (client)
        using (var response = await SendCreateAsync(
            client,
            csrf,
            Guid.NewGuid().ToString("D"),
            request))
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal(
                "portal-ticket-customer-email-invalid",
                body.RootElement.GetProperty("code").GetString());
            Assert.Equal(0, await CountTicketsAsync(request.Subject));
        }
    }

    [Fact]
    public async Task CreatePortalTicket_RejectsOversizedContentWithoutTruncatingOrPersisting()
    {
        var request = CreateRequest($"oversized-content-{Guid.NewGuid():N}") with
        {
            Content = new string('x', 262_145)
        };
        var (client, csrf, _) = await CookieAuthTestHelper.LoginAsSupportAsync(factory);

        using (client)
        using (var response = await SendCreateAsync(
            client,
            csrf,
            Guid.NewGuid().ToString("D"),
            request))
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal(
                "portal-ticket-content-too-long",
                body.RootElement.GetProperty("code").GetString());
            Assert.Equal(0, await CountTicketsAsync(request.Subject));
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
    public async Task CreatePortalTicket_ReplaysWhenEquivalentFieldsNormalizeToTheSamePayload()
    {
        var exactRequest = CreateRequest($"normalized-{Guid.NewGuid():N}");
        var paddedRequest = exactRequest with
        {
            Subject = $"  {exactRequest.Subject}  ",
            CustomerName = $"  {exactRequest.CustomerName}  ",
            CustomerEmail = $"  {exactRequest.CustomerEmail}  ",
            Content = $"  {exactRequest.Content}  "
        };
        var key = Guid.NewGuid().ToString();
        var createdTicketIds = new List<Guid>();
        var (client, csrf, _) = await CookieAuthTestHelper.LoginAsSupportAsync(factory);

        try
        {
            using var first = await SendCreateAsync(client, csrf, key, paddedRequest);
            using var replay = await SendCreateAsync(client, csrf, key, exactRequest);

            Assert.Equal(HttpStatusCode.Created, first.StatusCode);
            Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
            createdTicketIds.Add(await ReadTicketIdAsync(first));
            createdTicketIds.Add(await ReadTicketIdAsync(replay));
            Assert.Single(createdTicketIds.Distinct());
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
    public async Task CreatePortalTicket_UsesDedicatedPortalStateNotInboundEmailState()
    {
        var key = Guid.NewGuid().ToString();
        var request = CreateRequest($"dedicated-state-{Guid.NewGuid():N}");
        var createdTicketIds = new List<Guid>();
        var (client, csrf, userId) = await CookieAuthTestHelper.LoginAsSupportAsync(factory);

        try
        {
            using var response = await SendCreateAsync(client, csrf, key, request);

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            var ticketId = await ReadTicketIdAsync(response);
            createdTicketIds.Add(ticketId);

            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var portalRequest = await db.PortalTicketRequests
                .SingleAsync(candidate => candidate.UserId == userId && candidate.IdempotencyKey == key.ToLowerInvariant());

            Assert.Equal(ticketId, portalRequest.TicketId);
            Assert.Equal(64, portalRequest.RequestHash.Length);
            Assert.Empty(await db.ProcessedEmailMessages
                .Where(candidate => candidate.IdempotencyKey == key.ToLowerInvariant())
                .ToListAsync());
        }
        finally
        {
            await DeleteRequestArtifactsAsync(userId, key);
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

    private async Task DeleteRequestArtifactsAsync(Guid userId, string key)
    {
        var normalizedKey = key.ToLowerInvariant();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        try
        {
            await db.PortalTicketRequests
                .Where(request => request.UserId == userId && request.IdempotencyKey == normalizedKey)
                .ExecuteDeleteAsync();
            await db.ProcessedEmailMessages
                .Where(message => message.IdempotencyKey == normalizedKey)
                .ExecuteDeleteAsync();
        }
        catch (InvalidOperationException)
        {
            var portalRequests = await db.PortalTicketRequests
                .Where(request => request.UserId == userId && request.IdempotencyKey == normalizedKey)
                .ToListAsync();
            var processedMessages = await db.ProcessedEmailMessages
                .Where(message => message.IdempotencyKey == normalizedKey)
                .ToListAsync();
            db.PortalTicketRequests.RemoveRange(portalRequests);
            db.ProcessedEmailMessages.RemoveRange(processedMessages);
            await db.SaveChangesAsync();
        }
    }

    private sealed record PortalTicketRequest(
        string Subject,
        string CustomerName,
        string CustomerEmail,
        string Content);
}
