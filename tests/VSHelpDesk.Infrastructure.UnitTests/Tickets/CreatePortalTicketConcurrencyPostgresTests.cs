using Microsoft.EntityFrameworkCore;
using VSHelpDesk.Application.Abstractions.Authentication;
using VSHelpDesk.Application.Abstractions.Persistence;
using VSHelpDesk.Application.Abstractions.Persistence.Repositories;
using VSHelpDesk.Application.Abstractions.Security;
using VSHelpDesk.Application.Common.Localization;
using VSHelpDesk.Application.Features.Tickets.CreatePortalTicket;
using VSHelpDesk.Domain.Entities;
using VSHelpDesk.Domain.Enums;
using VSHelpDesk.Infrastructure.Authentication;
using VSHelpDesk.Infrastructure.Persistence;
using VSHelpDesk.Infrastructure.Persistence.Repositories;
using VSHelpDesk.Infrastructure.Persistence.Sequences;
using VSHelpDesk.Infrastructure.UnitTests.Persistence;

namespace VSHelpDesk.Infrastructure.UnitTests.Tickets;

public sealed class CreatePortalTicketConcurrencyPostgresTests
{
    [PostgresFact]
    public async Task ParallelIdenticalRequests_CreateOneTicketAndReplayTheWinner()
    {
        const int concurrentRequests = 8;
        var user = new User(
            "Portal idempotency test user",
            $"portal-idempotency-{Guid.NewGuid():N}",
            $"portal-idempotency-{Guid.NewGuid():N}@example.test",
            new PasswordHasher().Hash("unused-password"),
            UserRole.Support);
        var key = Guid.NewGuid().ToString("D");
        var subject = $"Concurrent portal ticket {Guid.NewGuid():N}";
        var command = new CreatePortalTicketCommand(
            key,
            subject,
            "Portal Customer",
            "portal-customer@example.test",
            "Concurrent portal content");

        await using (var seed = PostgresTestConnection.CreateContext())
        {
            seed.Users.Add(user);
            await seed.SaveChangesAsync();
        }

        try
        {
            var readBarrier = new PortalRequestReadBarrier(concurrentRequests);
            var attempts = Enumerable.Range(0, concurrentRequests)
                .Select(_ => RunAttemptAsync(user.Id, command, readBarrier))
                .ToArray();

            try
            {
                await readBarrier.AllInitialReadsCompleted.WaitAsync(TimeSpan.FromSeconds(15));
                Assert.Equal(concurrentRequests, readBarrier.InitialReadResults.Count);
                Assert.All(readBarrier.InitialReadResults, existing => Assert.False(existing));
            }
            finally
            {
                readBarrier.ReleaseWrites();
            }

            var results = await Task.WhenAll(attempts);
            Assert.All(results, result => Assert.True(result.IsSuccess));

            var ticketIds = results.Select(result => result.Value!.TicketId).Distinct().ToArray();
            Assert.Single(ticketIds);
            Assert.Single(results, result => !result.Value!.WasAlreadyProcessed);
            Assert.Equal(concurrentRequests - 1, results.Count(result => result.Value!.WasAlreadyProcessed));

            await using var verification = PostgresTestConnection.CreateContext();
            var storedRequests = await verification.PortalTicketRequests
                .Where(request => request.UserId == user.Id && request.IdempotencyKey == key)
                .ToListAsync();
            Assert.Single(storedRequests);
            Assert.Equal(ticketIds[0], storedRequests[0].TicketId);
            Assert.Equal(64, storedRequests[0].RequestHash.Length);
            Assert.Equal(1, await verification.Tickets.CountAsync(ticket => ticket.Id == ticketIds[0]));
            Assert.Equal(1, await verification.TicketMessages.CountAsync(message => message.TicketId == ticketIds[0]));
        }
        finally
        {
            await CleanupAsync(user.Id, key);
        }
    }

    [PostgresFact]
    public async Task ParallelDifferentPayloads_ReturnPayloadConflictForTheLosingRequest()
    {
        var user = new User(
            "Portal idempotency conflict user",
            $"portal-idempotency-conflict-{Guid.NewGuid():N}",
            $"portal-idempotency-conflict-{Guid.NewGuid():N}@example.test",
            new PasswordHasher().Hash("unused-password"),
            UserRole.Support);
        var key = Guid.NewGuid().ToString("D");
        var firstCommand = new CreatePortalTicketCommand(
            key,
            $"Concurrent conflict {Guid.NewGuid():N}",
            "Portal Customer",
            "portal-customer@example.test",
            "Original content");
        var changedCommand = firstCommand with { Content = "Changed content" };

        await using (var seed = PostgresTestConnection.CreateContext())
        {
            seed.Users.Add(user);
            await seed.SaveChangesAsync();
        }

        try
        {
            var readBarrier = new PortalRequestReadBarrier(participantCount: 2);
            var firstAttempt = RunAttemptAsync(user.Id, firstCommand, readBarrier);
            var changedAttempt = RunAttemptAsync(user.Id, changedCommand, readBarrier);

            try
            {
                await readBarrier.AllInitialReadsCompleted.WaitAsync(TimeSpan.FromSeconds(15));
                Assert.Equal([false, false], readBarrier.InitialReadResults.Order().ToArray());
            }
            finally
            {
                readBarrier.ReleaseWrites();
            }

            var results = await Task.WhenAll(firstAttempt, changedAttempt);
            var successful = Assert.Single(results, result => result.IsSuccess);
            var conflict = Assert.Single(
                results,
                result => result.Error == CreatePortalTicketErrorCodes.PayloadConflict);
            Assert.NotNull(successful.Value);
            Assert.Null(conflict.Value);

            await using var verification = PostgresTestConnection.CreateContext();
            Assert.Equal(
                1,
                await verification.PortalTicketRequests.CountAsync(
                    request => request.UserId == user.Id && request.IdempotencyKey == key));
            Assert.Equal(
                1,
                await verification.Tickets.CountAsync(ticket => ticket.Subject == firstCommand.Subject));
        }
        finally
        {
            await CleanupAsync(user.Id, key);
        }
    }

    private static async Task CleanupAsync(Guid userId, string key)
    {
        await using var cleanup = PostgresTestConnection.CreateContext();
        var ticketIds = await cleanup.PortalTicketRequests
            .Where(request => request.UserId == userId && request.IdempotencyKey == key)
            .Select(request => request.TicketId)
            .ToListAsync();

        await cleanup.PortalTicketRequests
            .Where(request => request.UserId == userId && request.IdempotencyKey == key)
            .ExecuteDeleteAsync();
        await cleanup.TicketMessages
            .Where(message => ticketIds.Contains(message.TicketId))
            .ExecuteDeleteAsync();
        await cleanup.Tickets
            .Where(ticket => ticketIds.Contains(ticket.Id))
            .ExecuteDeleteAsync();
        await cleanup.Users
            .Where(candidate => candidate.Id == userId)
            .ExecuteDeleteAsync();
    }

    private static async Task<VSHelpDesk.Application.Common.Models.Result<CreatePortalTicketResult>> RunAttemptAsync(
        Guid userId,
        CreatePortalTicketCommand command,
        PortalRequestReadBarrier readBarrier)
    {
        await using var context = PostgresTestConnection.CreateContext();
        var handler = new CreatePortalTicketHandler(
            new CoordinatedPortalTicketRequestRepository(
                new EfPortalTicketRequestRepository(context),
                readBarrier),
            new EfTicketRepository(context),
            new EfUnitOfWork(context),
            new TicketNumberGenerator(new PostgresSequenceAllocator(context)),
            TimeProvider.System,
            new FixedCurrentUserService(userId),
            new PostgresDatabaseErrorClassifier(),
            new PassthroughHtmlSanitizer(),
            new TestMessageProvider());

        return await handler.HandleAsync(command, CancellationToken.None);
    }

    private sealed class CoordinatedPortalTicketRequestRepository(
        EfPortalTicketRequestRepository inner,
        PortalRequestReadBarrier readBarrier) : IPortalTicketRequestRepository
    {
        public async Task<PortalTicketRequest?> GetByUserAndKeyAsync(
            Guid userId,
            string idempotencyKey,
            CancellationToken cancellationToken = default)
        {
            var existing = await inner.GetByUserAndKeyAsync(userId, idempotencyKey, cancellationToken);
            await readBarrier.WaitAfterInitialReadAsync(existing, cancellationToken);
            return existing;
        }

        public Task AddAsync(
            PortalTicketRequest request,
            CancellationToken cancellationToken = default) =>
            inner.AddAsync(request, cancellationToken);
    }

    private sealed class PortalRequestReadBarrier(int participantCount)
    {
        private readonly TaskCompletionSource allInitialReadsCompleted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource allowWrites =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly List<bool> initialReadResults = [];
        private int arrivedCount;

        public Task AllInitialReadsCompleted => allInitialReadsCompleted.Task;

        public IReadOnlyList<bool> InitialReadResults
        {
            get
            {
                lock (initialReadResults)
                {
                    return initialReadResults.ToArray();
                }
            }
        }

        public async Task WaitAfterInitialReadAsync(
            PortalTicketRequest? existing,
            CancellationToken cancellationToken)
        {
            var attemptNumber = Interlocked.Increment(ref arrivedCount);
            if (attemptNumber > participantCount)
            {
                return;
            }

            lock (initialReadResults)
            {
                initialReadResults.Add(existing is not null);
            }

            if (attemptNumber == participantCount)
            {
                allInitialReadsCompleted.TrySetResult();
            }

            await allowWrites.Task.WaitAsync(cancellationToken);
        }

        public void ReleaseWrites() => allowWrites.TrySetResult();
    }

    private sealed class FixedCurrentUserService(Guid userId) : ICurrentUserService
    {
        public Guid? UserId => userId;

        public bool IsAuthenticated => true;
    }

    private sealed class PassthroughHtmlSanitizer : IHtmlSanitizerService
    {
        public string SanitizeHtml(string inputHtml) => inputHtml;

        public string ToPlainText(string inputHtml) => inputHtml;
    }

    private sealed class TestMessageProvider : IMessageProvider
    {
        public string Get(string key) => key;

        public string Get(string key, params object[] args) => key;
    }
}
