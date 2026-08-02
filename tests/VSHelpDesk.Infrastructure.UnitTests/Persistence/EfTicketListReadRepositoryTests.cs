using Microsoft.EntityFrameworkCore;
using VSHelpDesk.Application.Features.Tickets.ReadModel;
using VSHelpDesk.Domain.Entities;
using VSHelpDesk.Domain.Enums;
using VSHelpDesk.Infrastructure.Persistence;
using VSHelpDesk.Infrastructure.Persistence.ReadModel;

namespace VSHelpDesk.Infrastructure.UnitTests.Persistence;

public sealed class EfTicketListReadRepositoryTests
{
    [PostgresFact]
    public async Task ReadAsync_UsesStableKeysetPagingAndLeavesChangeTrackerEmpty()
    {
        await using var context = PostgresTestConnection.CreateContext();
        var marker = $"page-{Guid.NewGuid():N}";
        var newest = new DateTime(2026, 8, 2, 12, 0, 0, DateTimeKind.Utc);
        var tickets = new[]
        {
            CreateTicket("TL-PAGE-001", marker, TicketStatus.New, newest),
            CreateTicket("TL-PAGE-002", marker, TicketStatus.New, newest),
            CreateTicket("TL-PAGE-003", marker, TicketStatus.New, newest),
            CreateTicket("TL-PAGE-004", marker, TicketStatus.New, newest.AddMinutes(-1)),
            CreateTicket("TL-PAGE-005", marker, TicketStatus.New, newest.AddMinutes(-2))
        };

        await context.Tickets.AddRangeAsync(tickets);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        try
        {
            var repository = new EfTicketListReadRepository(context);

            var first = await repository.ReadAsync(
                new TicketListReadRequest(null, marker, 3, null),
                CancellationToken.None);

            Assert.Equal(
                ["TL-PAGE-001", "TL-PAGE-002", "TL-PAGE-003"],
                first.Items.Select(item => item.TicketNumber));
            Assert.True(first.HasMore);
            Assert.Equal(
                new TicketListCursor(newest, "TL-PAGE-003"),
                first.NextCursor);

            var second = await repository.ReadAsync(
                new TicketListReadRequest(null, marker, 3, first.NextCursor),
                CancellationToken.None);

            Assert.Equal(
                ["TL-PAGE-004", "TL-PAGE-005"],
                second.Items.Select(item => item.TicketNumber));
            Assert.False(second.HasMore);
            Assert.Null(second.NextCursor);
            Assert.Equal(
                tickets.Select(ticket => ticket.Id).Order(),
                first.Items.Concat(second.Items).Select(item => item.Id).Order());
            Assert.Empty(context.ChangeTracker.Entries());
        }
        finally
        {
            await DeleteTicketsAsync(context, tickets);
        }
    }

    [PostgresFact]
    public async Task ReadAsync_SearchesAllFourFieldsCaseInsensitively()
    {
        await using var context = PostgresTestConnection.CreateContext();
        var token = Guid.NewGuid().ToString("N")[..8];
        var stamp = new DateTime(2026, 8, 2, 11, 0, 0, DateTimeKind.Utc);
        var tickets = new[]
        {
            Ticket.Create($"VS-SRCH-{token}-1", "plain", "Plain Customer", "plain-1@example.invalid", stamp),
            Ticket.Create($"VS-SRCH-{token}-2", $"SuBjEcT-{token}", "Plain Customer", "plain-2@example.invalid", stamp),
            Ticket.Create($"VS-SRCH-{token}-3", "plain", $"Çiğdem AkSoY-{token}", "plain-3@example.invalid", stamp),
            Ticket.Create($"VS-SRCH-{token}-4", "plain", "Plain Customer", $"EmaIL-{token}@Example.INVALID", stamp)
        };

        await context.Tickets.AddRangeAsync(tickets);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        try
        {
            var repository = new EfTicketListReadRepository(context);

            await AssertSingleSearchResultAsync(repository, $"vs-srch-{token}-1", tickets[0].Id);
            await AssertSingleSearchResultAsync(repository, $"subject-{token}", tickets[1].Id);
            await AssertSingleSearchResultAsync(repository, $"çIĞDEM aksoy-{token}", tickets[2].Id);
            await AssertSingleSearchResultAsync(repository, $"email-{token}@example.invalid", tickets[3].Id);
        }
        finally
        {
            await DeleteTicketsAsync(context, tickets);
        }
    }

    [PostgresFact]
    public async Task ReadAsync_TreatsPercentUnderscoreAndBackslashAsLiteralSearchText()
    {
        await using var context = PostgresTestConnection.CreateContext();
        var token = Guid.NewGuid().ToString("N")[..8];
        var stamp = new DateTime(2026, 8, 2, 10, 0, 0, DateTimeKind.Utc);
        var tickets = new[]
        {
            Ticket.Create($"TL-LIT-{token}-1", $"{token}%literal", "Plain", "plain-1@example.invalid", stamp),
            Ticket.Create($"TL-LIT-{token}-2", $"{token}Xliteral", "Plain", "plain-2@example.invalid", stamp),
            Ticket.Create($"TL-LIT-{token}-3", $"{token}_literal", "Plain", "plain-3@example.invalid", stamp),
            Ticket.Create($"TL-LIT-{token}-4", $"{token}Yliteral", "Plain", "plain-4@example.invalid", stamp),
            Ticket.Create($"TL-LIT-{token}-5", $"{token}\\literal", "Plain", "plain-5@example.invalid", stamp),
            Ticket.Create($"TL-LIT-{token}-6", $"{token}Zliteral", "Plain", "plain-6@example.invalid", stamp)
        };

        await context.Tickets.AddRangeAsync(tickets);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        try
        {
            var repository = new EfTicketListReadRepository(context);

            await AssertSingleSearchResultAsync(repository, $"{token}%literal", tickets[0].Id);
            await AssertSingleSearchResultAsync(repository, $"{token}_literal", tickets[2].Id);
            await AssertSingleSearchResultAsync(repository, $"{token}\\literal", tickets[4].Id);
        }
        finally
        {
            await DeleteTicketsAsync(context, tickets);
        }
    }

    [PostgresFact]
    public async Task ReadAsync_ComputesSearchedCountsBeforeApplyingSelectedStatus()
    {
        await using var context = PostgresTestConnection.CreateContext();
        var marker = $"counts-{Guid.NewGuid():N}";
        var stamp = new DateTime(2026, 8, 2, 9, 0, 0, DateTimeKind.Utc);
        var tickets = new[]
        {
            CreateTicket("TL-COUNT-001", marker, TicketStatus.New, stamp),
            CreateTicket("TL-COUNT-002", marker, TicketStatus.WaitingCustomerReply, stamp.AddMinutes(-1)),
            CreateTicket("TL-COUNT-003", marker, TicketStatus.CustomerReplied, stamp.AddMinutes(-2)),
            CreateTicket("TL-COUNT-004", marker, TicketStatus.Resolved, stamp.AddMinutes(-3))
        };

        await context.Tickets.AddRangeAsync(tickets);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        try
        {
            var repository = new EfTicketListReadRepository(context);

            var result = await repository.ReadAsync(
                new TicketListReadRequest(TicketStatus.Resolved, marker, 10, null),
                CancellationToken.None);

            var item = Assert.Single(result.Items);
            Assert.Equal("TL-COUNT-004", item.TicketNumber);
            Assert.Equal("Resolved", item.Status);
            Assert.Equal(4, result.Counts.All);
            Assert.Equal(1, result.Counts.New);
            Assert.Equal(1, result.Counts.WaitingCustomerReply);
            Assert.Equal(1, result.Counts.CustomerReplied);
            Assert.Equal(1, result.Counts.Resolved);
        }
        finally
        {
            await DeleteTicketsAsync(context, tickets);
        }
    }

    [PostgresFact]
    public async Task ReadAsync_ObservesCancellation()
    {
        await using var context = PostgresTestConnection.CreateContext();
        var repository = new EfTicketListReadRepository(context);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            repository.ReadAsync(
                new TicketListReadRequest(null, null, 10, null),
                cancellation.Token));
    }

    private static Ticket CreateTicket(
        string ticketNumber,
        string marker,
        TicketStatus status,
        DateTime lastActivityAt)
    {
        var ticket = Ticket.Create(
            ticketNumber,
            $"Ticket list {marker}",
            "Performance Customer",
            "customer@example.invalid",
            lastActivityAt.AddHours(-1));

        switch (status)
        {
            case TicketStatus.New:
                ticket.RecordMessageActivity(lastActivityAt);
                break;
            case TicketStatus.WaitingCustomerReply:
                ticket.MarkAsWaitingCustomerReply(lastActivityAt);
                break;
            case TicketStatus.CustomerReplied:
                ticket.MarkAsCustomerReplied(lastActivityAt);
                break;
            case TicketStatus.Resolved:
                ticket.MarkAsWaitingCustomerReply(lastActivityAt.AddMinutes(-1));
                ticket.ResolveAutomatically(lastActivityAt);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(status), status, null);
        }

        return ticket;
    }

    private static async Task AssertSingleSearchResultAsync(
        EfTicketListReadRepository repository,
        string search,
        Guid expectedId)
    {
        var result = await repository.ReadAsync(
            new TicketListReadRequest(null, search, 10, null),
            CancellationToken.None);

        var item = Assert.Single(result.Items);
        Assert.Equal(expectedId, item.Id);
    }

    private static async Task DeleteTicketsAsync(
        ApplicationDbContext context,
        IReadOnlyCollection<Ticket> tickets)
    {
        context.ChangeTracker.Clear();
        var ticketIds = tickets.Select(ticket => ticket.Id).ToArray();
        await context.Tickets
            .Where(ticket => ticketIds.Contains(ticket.Id))
            .ExecuteDeleteAsync();
    }
}
