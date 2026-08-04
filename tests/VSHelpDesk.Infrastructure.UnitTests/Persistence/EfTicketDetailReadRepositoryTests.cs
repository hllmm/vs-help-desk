using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using VSHelpDesk.Application.Features.Tickets.ReadModel;
using VSHelpDesk.Domain.Entities;
using VSHelpDesk.Domain.Enums;
using VSHelpDesk.Infrastructure.Persistence;
using VSHelpDesk.Infrastructure.Persistence.ReadModel;

namespace VSHelpDesk.Infrastructure.UnitTests.Persistence;

public sealed class EfTicketDetailReadRepositoryTests
{
    private static readonly DateTime T0 = new(2026, 8, 2, 8, 0, 0, DateTimeKind.Utc);

    [PostgresFact]
    public async Task Reads_205MessageHistoryInStableBoundedChronologicalPagesWithoutTracking()
    {
        var fixture = await SeedLongHistoryAsync();

        try
        {
            await using var context = PostgresTestConnection.CreateContext();
            var repository = new EfTicketDetailReadRepository(context);
            var storageOrder = fixture.Messages
                .OrderByDescending(message => message.CreatedAt)
                .ThenByDescending(message => message.Id)
                .ToList();

            var detail = await repository.ReadDetailsAsync(
                fixture.Ticket.Id,
                100,
                CancellationToken.None);

            Assert.NotNull(detail);
            Assert.Equal(fixture.Ticket.Id, detail.Details.Id);
            Assert.Equal(fixture.Ticket.TicketNumber, detail.Details.TicketNumber);
            Assert.Equal(
                storageOrder.Take(100).Reverse().Select(message => message.Id),
                detail.Details.Messages.Select(message => message.Id));
            Assert.True(detail.HasMoreMessages);
            Assert.Equal(
                new TicketMessageCursor(storageOrder[99].CreatedAt, storageOrder[99].Id),
                detail.NextCursor);
            Assert.Equal(
                ExpectedAttachmentIds(fixture.Attachments, storageOrder.Take(100)),
                detail.Details.Attachments.Select(attachment => attachment.Id));
            Assert.All(
                detail.Details.Attachments,
                attachment => Assert.Contains(
                    attachment.TicketMessageId,
                    detail.Details.Messages.Select(message => message.Id)));
            Assert.Empty(context.ChangeTracker.Entries());

            var second = await repository.ReadMessagesAsync(
                fixture.Ticket.Id,
                new TicketMessageReadRequest(100, detail.NextCursor),
                CancellationToken.None);

            Assert.NotNull(second);
            Assert.Equal(
                storageOrder.Skip(100).Take(100).Reverse().Select(message => message.Id),
                second.Messages.Select(message => message.Id));
            Assert.True(second.HasMore);
            Assert.Equal(
                new TicketMessageCursor(storageOrder[199].CreatedAt, storageOrder[199].Id),
                second.NextCursor);
            Assert.Equal(
                ExpectedAttachmentIds(fixture.Attachments, storageOrder.Skip(100).Take(100)),
                second.Attachments.Select(attachment => attachment.Id));
            Assert.Empty(context.ChangeTracker.Entries());

            var third = await repository.ReadMessagesAsync(
                fixture.Ticket.Id,
                new TicketMessageReadRequest(100, second.NextCursor),
                CancellationToken.None);

            Assert.NotNull(third);
            Assert.Equal(
                storageOrder.Skip(200).Take(5).Reverse().Select(message => message.Id),
                third.Messages.Select(message => message.Id));
            Assert.False(third.HasMore);
            Assert.Null(third.NextCursor);
            Assert.Equal(
                ExpectedAttachmentIds(fixture.Attachments, storageOrder.Skip(200).Take(5)),
                third.Attachments.Select(attachment => attachment.Id));
            Assert.Empty(context.ChangeTracker.Entries());

            var allPresentedIds = third.Messages
                .Concat(second.Messages)
                .Concat(detail.Details.Messages)
                .Select(message => message.Id)
                .ToArray();
            Assert.Equal(
                fixture.Messages
                    .OrderBy(message => message.CreatedAt)
                    .ThenBy(message => message.Id)
                    .Select(message => message.Id),
                allPresentedIds);
            Assert.Equal(205, allPresentedIds.Distinct().Count());
        }
        finally
        {
            await DeleteFixtureAsync(fixture.Ticket.Id);
        }
    }

    [PostgresFact]
    public async Task Reads_MissingTicket_ReturnNullAndLeaveChangeTrackerEmpty()
    {
        await using var context = PostgresTestConnection.CreateContext();
        var repository = new EfTicketDetailReadRepository(context);
        var missingTicketId = Guid.NewGuid();

        var detail = await repository.ReadDetailsAsync(
            missingTicketId,
            100,
            CancellationToken.None);
        var messages = await repository.ReadMessagesAsync(
            missingTicketId,
            new TicketMessageReadRequest(100, null),
            CancellationToken.None);

        Assert.Null(detail);
        Assert.Null(messages);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    [PostgresFact]
    public async Task Reads_PassCancellationTokenToEveryEfAsyncOperation()
    {
        var fixture = await SeedLongHistoryAsync();

        try
        {
            var interceptor = new RecordingCancellationInterceptor();
            var connectionString = PostgresTestConnection.TryGet()!;
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseNpgsql(connectionString)
                .AddInterceptors(interceptor)
                .Options;
            await using var context = new ApplicationDbContext(options);
            var repository = new EfTicketDetailReadRepository(context);
            using var cancellation = new CancellationTokenSource();

            var detail = await repository.ReadDetailsAsync(
                fixture.Ticket.Id,
                100,
                cancellation.Token);
            Assert.NotNull(detail);
            var page = await repository.ReadMessagesAsync(
                fixture.Ticket.Id,
                new TicketMessageReadRequest(100, detail.NextCursor),
                cancellation.Token);
            Assert.NotNull(page);

            Assert.Equal(6, interceptor.ReaderCancellationTokens.Count);
            Assert.All(
                interceptor.ReaderCancellationTokens,
                token => Assert.Equal(cancellation.Token, token));

            using var alreadyCancelled = new CancellationTokenSource();
            alreadyCancelled.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                repository.ReadDetailsAsync(fixture.Ticket.Id, 100, alreadyCancelled.Token));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                repository.ReadMessagesAsync(
                    fixture.Ticket.Id,
                    new TicketMessageReadRequest(100, null),
                    alreadyCancelled.Token));
        }
        finally
        {
            await DeleteFixtureAsync(fixture.Ticket.Id);
        }
    }

    private static async Task<LongHistoryFixture> SeedLongHistoryAsync()
    {
        await using var context = PostgresTestConnection.CreateContext();
        var ticket = Ticket.Create(
            $"TD-{Guid.NewGuid():N}"[..16],
            "Bounded detail history",
            "Performance Customer",
            "detail-history@example.invalid",
            T0);
        var messages = Enumerable.Range(1, 205)
            .Select(index => new TicketMessage(
                ticket.Id,
                index % 2 == 0 ? MessageSenderType.Support : MessageSenderType.Customer,
                $"Literal message {index} <tag>",
                isHtml: false,
                createdAtUtc: T0.AddMinutes((index - 1) / 2)))
            .ToList();
        var attachmentMessageIndexes = new[] { 1, 100, 101, 205 };
        var attachments = attachmentMessageIndexes
            .Select(index => new TicketAttachment(
                messages[index - 1].Id,
                $"message-{index}.txt",
                $"stored-{Guid.NewGuid():N}.txt",
                $"/tmp/vshd-test-{Guid.NewGuid():N}.txt",
                "text/plain",
                index,
                messages[index - 1].CreatedAt.AddSeconds(1)))
            .ToList();

        context.Add(ticket);
        context.AddRange(messages);
        context.AddRange(attachments);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        return new LongHistoryFixture(ticket, messages, attachments);
    }

    private static IReadOnlyList<Guid> ExpectedAttachmentIds(
        IReadOnlyCollection<TicketAttachment> attachments,
        IEnumerable<TicketMessage> pageMessages)
    {
        var messageIds = pageMessages.Select(message => message.Id).ToHashSet();
        return attachments
            .Where(attachment => messageIds.Contains(attachment.TicketMessageId))
            .OrderBy(attachment => attachment.CreatedAt)
            .ThenBy(attachment => attachment.Id)
            .Select(attachment => attachment.Id)
            .ToArray();
    }

    private static async Task DeleteFixtureAsync(Guid ticketId)
    {
        await using var context = PostgresTestConnection.CreateContext();
        var messageIds = await context.TicketMessages
            .Where(message => message.TicketId == ticketId)
            .Select(message => message.Id)
            .ToArrayAsync();
        await context.TicketAttachments
            .Where(attachment => messageIds.Contains(attachment.TicketMessageId))
            .ExecuteDeleteAsync();
        await context.TicketMessages
            .Where(message => message.TicketId == ticketId)
            .ExecuteDeleteAsync();
        await context.Tickets
            .Where(ticket => ticket.Id == ticketId)
            .ExecuteDeleteAsync();
    }

    private sealed record LongHistoryFixture(
        Ticket Ticket,
        IReadOnlyList<TicketMessage> Messages,
        IReadOnlyList<TicketAttachment> Attachments);

    private sealed class RecordingCancellationInterceptor : DbCommandInterceptor
    {
        public List<CancellationToken> ReaderCancellationTokens { get; } = [];

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken)
        {
            ReaderCancellationTokens.Add(cancellationToken);
            return base.ReaderExecutingAsync(
                command,
                eventData,
                result,
                cancellationToken);
        }
    }
}
