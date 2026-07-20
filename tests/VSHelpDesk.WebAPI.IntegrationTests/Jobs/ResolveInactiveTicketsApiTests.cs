using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using VSHelpDesk.Domain.Entities;
using VSHelpDesk.Domain.Enums;
using VSHelpDesk.Infrastructure.Persistence;

namespace VSHelpDesk.WebAPI.IntegrationTests.Jobs;

public sealed class ResolveInactiveTicketsApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly DateTimeOffset FixedNow =
        new(2026, 8, 11, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTime CutoffUtc = FixedNow.UtcDateTime.AddDays(-3);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly WebApplicationFactory<Program> baseFactory;

    public ResolveInactiveTicketsApiTests(WebApplicationFactory<Program> factory)
    {
        baseFactory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("environment", "Development");
            builder.UseEnvironment("Development");
        });
    }

    [Fact]
    public async Task ResolveInactive_WithoutOrWrongKey_Returns401WithoutChangingRows()
    {
        await using var factory = CreateFactoryWithFixedTime();
        var token = Guid.NewGuid().ToString("N")[..8];
        var ticketIds = new List<Guid>();

        try
        {
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var ticket = Ticket.Create(
                    $"VS-R4{token}",
                    "Auth guard waiting",
                    "Ada",
                    $"auth-{token}@example.test",
                    CutoffUtc.AddDays(-1));
                ticket.MarkAsWaitingCustomerReply(CutoffUtc);
                db.Add(ticket);
                await db.SaveChangesAsync();
                ticketIds.Add(ticket.Id);
            }

            using var client = factory.CreateClient();

            using var missingKeyResponse = await client.PostAsync(
                "/api/jobs/resolve-inactive-tickets",
                content: null);
            Assert.Equal(HttpStatusCode.Unauthorized, missingKeyResponse.StatusCode);

            using var wrongKeyRequest = new HttpRequestMessage(
                HttpMethod.Post,
                "/api/jobs/resolve-inactive-tickets");
            wrongKeyRequest.Headers.Add("X-Jobs-Api-Key", "definitely-not-the-jobs-key");
            using var wrongKeyResponse = await client.SendAsync(wrongKeyRequest);
            Assert.Equal(HttpStatusCode.Unauthorized, wrongKeyResponse.StatusCode);

            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var ticket = await db.Tickets.SingleAsync(t => t.Id == ticketIds[0]);
                Assert.Equal(TicketStatus.WaitingCustomerReply, ticket.Status);
                Assert.Null(ticket.ClosedByUserId);
                Assert.Null(ticket.ResolvedAt);
            }
        }
        finally
        {
            await CleanupTicketsAsync(factory, ticketIds);
        }
    }

    [Fact]
    public async Task ResolveInactive_ExactBoundary_ResolvesOnlyEligibleWaitingRows()
    {
        await using var factory = CreateFactoryWithFixedTime();
        var apiKey = GetJobsApiKey(factory);
        var token = Guid.NewGuid().ToString("N")[..8];
        var ticketIds = new List<Guid>();
        var parked = new List<ParkedTicket>();
        Guid notDueId = default;
        Guid exactDueId = default;
        Guid beyondDueId = default;
        Guid newId = default;
        Guid customerRepliedId = default;
        Guid resolvedId = default;
        var seedUserId = Guid.Empty;

        try
        {
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                seedUserId = await db.Users.Select(u => u.Id).FirstAsync();

                var notDue = Ticket.Create(
                    $"VS-ND{token}",
                    "Not due",
                    "Ada",
                    $"nd-{token}@example.test",
                    CutoffUtc.AddDays(-1));
                // 1µs past cutoff (brief: 12:00:00.000001Z); PG timestamp precision is µs, not ticks.
                notDue.MarkAsWaitingCustomerReply(CutoffUtc.AddMicroseconds(1));

                var exactDue = Ticket.Create(
                    $"VS-EX{token}",
                    "Exact due",
                    "Ada",
                    $"ex-{token}@example.test",
                    CutoffUtc.AddDays(-1));
                exactDue.MarkAsWaitingCustomerReply(CutoffUtc);

                var beyondDue = Ticket.Create(
                    $"VS-BY{token}",
                    "Beyond due",
                    "Ada",
                    $"by-{token}@example.test",
                    CutoffUtc.AddDays(-1));
                beyondDue.MarkAsWaitingCustomerReply(CutoffUtc.AddSeconds(-1));

                var stillNew = Ticket.Create(
                    $"VS-NW{token}",
                    "Still new",
                    "Ada",
                    $"nw-{token}@example.test",
                    CutoffUtc.AddDays(-10));

                var customerReplied = Ticket.Create(
                    $"VS-CR{token}",
                    "Customer replied",
                    "Ada",
                    $"cr-{token}@example.test",
                    CutoffUtc.AddDays(-10));
                customerReplied.MarkAsCustomerReplied(CutoffUtc.AddDays(-5));

                var alreadyResolved = Ticket.Create(
                    $"VS-RS{token}",
                    "Already resolved",
                    "Ada",
                    $"rs-{token}@example.test",
                    CutoffUtc.AddDays(-10));
                Assert.True(alreadyResolved.ResolveManually(CutoffUtc.AddDays(-4), seedUserId));

                db.AddRange(
                    notDue,
                    exactDue,
                    beyondDue,
                    stillNew,
                    customerReplied,
                    alreadyResolved);
                await db.SaveChangesAsync();

                notDueId = notDue.Id;
                exactDueId = exactDue.Id;
                beyondDueId = beyondDue.Id;
                newId = stillNew.Id;
                customerRepliedId = customerReplied.Id;
                resolvedId = alreadyResolved.Id;
                ticketIds.AddRange(
                [
                    notDueId,
                    exactDueId,
                    beyondDueId,
                    newId,
                    customerRepliedId,
                    resolvedId
                ]);

                parked = await ParkForeignEligibleAsync(db, CutoffUtc, ticketIds);
            }

            using var client = factory.CreateClient();
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                "/api/jobs/resolve-inactive-tickets");
            request.Headers.Add("X-Jobs-Api-Key", apiKey);
            using var response = await client.SendAsync(request);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var payload = await response.Content.ReadFromJsonAsync<JobPayload>(JsonOptions);
            Assert.NotNull(payload);
            AssertEqualUtc(CutoffUtc, payload.CutoffUtc);
            Assert.Equal(2, payload.Candidates);
            Assert.Equal(2, payload.Resolved);
            Assert.Equal(0, payload.Skipped);
            Assert.Equal(0, payload.Conflicted);
            Assert.Equal(0, payload.Failed);

            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                var notDue = await db.Tickets.SingleAsync(t => t.Id == notDueId);
                Assert.Equal(TicketStatus.WaitingCustomerReply, notDue.Status);
                Assert.Null(notDue.ClosedByUserId);
                Assert.Null(notDue.ResolvedAt);
                AssertEqualUtc(CutoffUtc.AddMicroseconds(1), notDue.WaitingCustomerSince!.Value);

                var exactDue = await db.Tickets.SingleAsync(t => t.Id == exactDueId);
                Assert.Equal(TicketStatus.Resolved, exactDue.Status);
                Assert.Null(exactDue.ClosedByUserId);
                AssertEqualUtc(FixedNow.UtcDateTime, exactDue.ResolvedAt!.Value);
                AssertEqualUtc(FixedNow.UtcDateTime, exactDue.UpdatedAt);
                AssertEqualUtc(FixedNow.UtcDateTime, exactDue.LastActivityAt);
                Assert.Null(exactDue.WaitingCustomerSince);

                var beyondDue = await db.Tickets.SingleAsync(t => t.Id == beyondDueId);
                Assert.Equal(TicketStatus.Resolved, beyondDue.Status);
                Assert.Null(beyondDue.ClosedByUserId);
                AssertEqualUtc(FixedNow.UtcDateTime, beyondDue.ResolvedAt!.Value);
                Assert.Null(beyondDue.WaitingCustomerSince);

                var stillNew = await db.Tickets.SingleAsync(t => t.Id == newId);
                Assert.Equal(TicketStatus.New, stillNew.Status);
                Assert.Null(stillNew.ClosedByUserId);

                var customerReplied = await db.Tickets.SingleAsync(t => t.Id == customerRepliedId);
                Assert.Equal(TicketStatus.CustomerReplied, customerReplied.Status);
                Assert.Null(customerReplied.ClosedByUserId);

                var alreadyResolved = await db.Tickets.SingleAsync(t => t.Id == resolvedId);
                Assert.Equal(TicketStatus.Resolved, alreadyResolved.Status);
                Assert.Equal(seedUserId, alreadyResolved.ClosedByUserId);
            }
        }
        finally
        {
            await CleanupTicketsAsync(factory, ticketIds);
            await RestoreParkedAsync(factory, parked);
        }
    }

    [Fact]
    public async Task ResolveInactive_ZeroCandidates_ReturnsExactZeroSummary()
    {
        await using var factory = CreateFactoryWithFixedTime();
        var apiKey = GetJobsApiKey(factory);
        var token = Guid.NewGuid().ToString("N")[..8];
        var ticketIds = new List<Guid>();
        var parked = new List<ParkedTicket>();

        try
        {
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                // Isolate from shared-DB pollution left by other suites.
                parked = await ParkForeignEligibleAsync(db, CutoffUtc, keepIds: []);

                var recent = Ticket.Create(
                    $"VS-ZR{token}",
                    "Recent waiting",
                    "Ada",
                    $"zr-{token}@example.test",
                    CutoffUtc.AddDays(-1));
                recent.MarkAsWaitingCustomerReply(CutoffUtc.AddHours(1));
                db.Add(recent);
                await db.SaveChangesAsync();
                ticketIds.Add(recent.Id);
            }

            using var client = factory.CreateClient();
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                "/api/jobs/resolve-inactive-tickets");
            request.Headers.Add("X-Jobs-Api-Key", apiKey);
            using var response = await client.SendAsync(request);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var payload = await response.Content.ReadFromJsonAsync<JobPayload>(JsonOptions);
            Assert.NotNull(payload);
            AssertEqualUtc(CutoffUtc, payload.CutoffUtc);
            Assert.Equal(0, payload.Candidates);
            Assert.Equal(0, payload.Resolved);
            Assert.Equal(0, payload.Skipped);
            Assert.Equal(0, payload.Conflicted);
            Assert.Equal(0, payload.Failed);

            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var recent = await db.Tickets.SingleAsync(t => t.Id == ticketIds[0]);
                Assert.Equal(TicketStatus.WaitingCustomerReply, recent.Status);
            }
        }
        finally
        {
            await CleanupTicketsAsync(factory, ticketIds);
            await RestoreParkedAsync(factory, parked);
        }
    }

    [Fact]
    public async Task ResolveInactive_SetsNullCloserAndExactRunTimestamp()
    {
        await using var factory = CreateFactoryWithFixedTime();
        var apiKey = GetJobsApiKey(factory);
        var token = Guid.NewGuid().ToString("N")[..8];
        var ticketIds = new List<Guid>();
        var parked = new List<ParkedTicket>();

        try
        {
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var ticket = Ticket.Create(
                    $"VS-CL{token}",
                    "Closer null",
                    "Ada",
                    $"cl-{token}@example.test",
                    CutoffUtc.AddDays(-1));
                ticket.MarkAsWaitingCustomerReply(CutoffUtc.AddHours(-6));
                db.Add(ticket);
                await db.SaveChangesAsync();
                ticketIds.Add(ticket.Id);
                parked = await ParkForeignEligibleAsync(db, CutoffUtc, ticketIds);
            }

            using var client = factory.CreateClient();
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                "/api/jobs/resolve-inactive-tickets");
            request.Headers.Add("X-Jobs-Api-Key", apiKey);
            using var response = await client.SendAsync(request);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var payload = await response.Content.ReadFromJsonAsync<JobPayload>(JsonOptions);
            Assert.NotNull(payload);
            Assert.Equal(1, payload.Candidates);
            Assert.Equal(1, payload.Resolved);
            Assert.Equal(0, payload.Skipped);
            Assert.Equal(0, payload.Conflicted);
            Assert.Equal(0, payload.Failed);

            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var ticket = await db.Tickets.SingleAsync(t => t.Id == ticketIds[0]);
                Assert.Equal(TicketStatus.Resolved, ticket.Status);
                Assert.Null(ticket.ClosedByUserId);
                AssertEqualUtc(FixedNow.UtcDateTime, ticket.ResolvedAt!.Value);
                AssertEqualUtc(FixedNow.UtcDateTime, ticket.UpdatedAt);
                AssertEqualUtc(FixedNow.UtcDateTime, ticket.LastActivityAt);
                Assert.Null(ticket.WaitingCustomerSince);
            }
        }
        finally
        {
            await CleanupTicketsAsync(factory, ticketIds);
            await RestoreParkedAsync(factory, parked);
        }
    }

    private WebApplicationFactory<Program> CreateFactoryWithFixedTime() =>
        baseFactory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("environment", "Development");
            builder.UseEnvironment("Development");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(new FixedTimeProvider(FixedNow));
            });
        });

    private static string GetJobsApiKey(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var apiKey = configuration["Jobs:ApiKey"];
        Assert.False(string.IsNullOrWhiteSpace(apiKey));
        return apiKey!;
    }

    private static void AssertEqualUtc(DateTime expected, DateTime actual)
    {
        Assert.Equal(
            DateTime.SpecifyKind(expected, DateTimeKind.Utc),
            DateTime.SpecifyKind(actual, DateTimeKind.Utc));
    }

    /// <summary>
    /// Temporarily moves shared-DB eligible rows past the cutoff so exact candidate counts
    /// are deterministic. Restored in finally; never truncates.
    /// </summary>
    private static async Task<List<ParkedTicket>> ParkForeignEligibleAsync(
        ApplicationDbContext db,
        DateTime cutoffUtc,
        IReadOnlyCollection<Guid> keepIds)
    {
        var foreign = await db.Tickets
            .Where(ticket =>
                ticket.Status == TicketStatus.WaitingCustomerReply
                && ticket.WaitingCustomerSince != null
                && ticket.WaitingCustomerSince <= cutoffUtc
                && !keepIds.Contains(ticket.Id))
            .ToListAsync();

        var parked = new List<ParkedTicket>(foreign.Count);
        var parkUntil = cutoffUtc.AddDays(30);
        foreach (var ticket in foreign)
        {
            parked.Add(new ParkedTicket(ticket.Id, ticket.WaitingCustomerSince));
            db.Entry(ticket).Property(nameof(Ticket.WaitingCustomerSince)).CurrentValue = parkUntil;
            db.Entry(ticket).Property(nameof(Ticket.WaitingCustomerSince)).IsModified = true;
        }

        if (foreign.Count > 0)
        {
            await db.SaveChangesAsync();
        }

        return parked;
    }

    private static async Task RestoreParkedAsync(
        WebApplicationFactory<Program> factory,
        IReadOnlyList<ParkedTicket> parked)
    {
        if (parked.Count == 0)
        {
            return;
        }

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        foreach (var item in parked)
        {
            var ticket = await db.Tickets.FindAsync(item.Id);
            if (ticket is null)
            {
                continue;
            }

            db.Entry(ticket).Property(nameof(Ticket.WaitingCustomerSince)).CurrentValue =
                item.OriginalWaitingCustomerSince;
            db.Entry(ticket).Property(nameof(Ticket.WaitingCustomerSince)).IsModified = true;
        }

        await db.SaveChangesAsync();
    }

    private static async Task CleanupTicketsAsync(
        WebApplicationFactory<Program> factory,
        IReadOnlyList<Guid> ticketIds)
    {
        if (ticketIds.Count == 0)
        {
            return;
        }

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var messages = await db.TicketMessages
            .Where(m => ticketIds.Contains(m.TicketId))
            .ToListAsync();
        db.TicketMessages.RemoveRange(messages);

        var processed = await db.ProcessedEmailMessages
            .Where(p => p.TicketId != null && ticketIds.Contains(p.TicketId.Value))
            .ToListAsync();
        db.ProcessedEmailMessages.RemoveRange(processed);

        var tickets = await db.Tickets
            .Where(t => ticketIds.Contains(t.Id))
            .ToListAsync();
        db.Tickets.RemoveRange(tickets);

        await db.SaveChangesAsync();
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed record ParkedTicket(Guid Id, DateTime? OriginalWaitingCustomerSince);

    private sealed record JobPayload(
        DateTime CutoffUtc,
        int Candidates,
        int Resolved,
        int Skipped,
        int Conflicted,
        int Failed);
}
