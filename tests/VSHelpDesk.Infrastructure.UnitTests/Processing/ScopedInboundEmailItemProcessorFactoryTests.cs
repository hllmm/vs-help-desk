using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using VSHelpDesk.Application.Abstractions.Email;
using VSHelpDesk.Application.Abstractions.Persistence;
using VSHelpDesk.Application.Abstractions.Persistence.Repositories;
using VSHelpDesk.Application.Features.MailProcessing;
using VSHelpDesk.Application.Features.MailProcessing.Acknowledgements;
using VSHelpDesk.Application.Features.MailProcessing.ProcessIncomingEmails;
using VSHelpDesk.Domain.Entities;
using VSHelpDesk.Infrastructure.Processing;

namespace VSHelpDesk.Infrastructure.UnitTests.Processing;

public sealed class ScopedInboundEmailItemProcessorFactoryTests
{
    [Fact]
    public async Task Factory_CreatesAndDisposesDistinctAsyncScopeForEachReceipt()
    {
        var tracker = new ScopeTracker();
        var services = new ServiceCollection();
        services.AddSingleton(tracker);
        services.AddScoped<IInboundEmailItemProcessor, TrackingProcessor>();
        var root = services.BuildServiceProvider();
        var scopeFactory = new TrackingServiceScopeFactory(root, tracker);
        var factory = new ScopedInboundEmailItemProcessorFactory(scopeFactory);

        var first = await factory.ProcessAsync(Mail("fake\0one"), CancellationToken.None);
        var second = await factory.ProcessAsync(Mail("fake\0two"), CancellationToken.None);

        Assert.Equal(InboundEmailItemOutcome.CreatedTicket, first.Outcome);
        Assert.Equal(InboundEmailItemOutcome.CreatedTicket, second.Outcome);
        Assert.Equal(2, tracker.CreatedScopeIds.Count);
        Assert.NotEqual(tracker.CreatedScopeIds[0], tracker.CreatedScopeIds[1]);
        Assert.Equal(2, tracker.DisposedScopeIds.Count);
        Assert.Equal(tracker.CreatedScopeIds, tracker.DisposedScopeIds);
        Assert.Equal(["fake\0one", "fake\0two"], tracker.ProcessedReceiptValues);
        Assert.Equal(2, tracker.ProcessorInstanceIds.Count);
        Assert.NotEqual(tracker.ProcessorInstanceIds[0], tracker.ProcessorInstanceIds[1]);
    }

    [Fact]
    public async Task RetryDueAcknowledgements_UsesFreshScopeAndDisposesIt()
    {
        var tracker = new ScopeTracker();
        var services = new ServiceCollection();
        services.AddSingleton(tracker);
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<EmptyDb>();
        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<EmptyDb>());
        services.AddScoped<IProcessedEmailRepository>(sp => sp.GetRequiredService<EmptyDb>());
        services.AddScoped<ITicketRepository>(sp => sp.GetRequiredService<EmptyDb>());
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<EmptyDb>());
        services.AddScoped<IEmailSender, NoopSender>();
        services.AddScoped<AcknowledgementDispatcher>(sp =>
        {
            tracker.DispatcherResolveCount++;
            return new AcknowledgementDispatcher(
                sp.GetRequiredService<IProcessedEmailRepository>(),
                sp.GetRequiredService<ITicketRepository>(),
                sp.GetRequiredService<IUnitOfWork>(),
                sp.GetRequiredService<IEmailSender>(),
                sp.GetRequiredService<TimeProvider>(),
                NullLogger<AcknowledgementDispatcher>.Instance);
        });
        var root = services.BuildServiceProvider();
        var scopeFactory = new TrackingServiceScopeFactory(root, tracker);
        var factory = new ScopedInboundEmailItemProcessorFactory(scopeFactory);

        var summary = await factory.RetryDueAcknowledgementsAsync(CancellationToken.None);

        Assert.Equal(0, summary.Attempted);
        Assert.Equal(0, summary.Sent);
        Assert.Equal(0, summary.Failed);
        Assert.Single(tracker.CreatedScopeIds);
        Assert.Single(tracker.DisposedScopeIds);
        Assert.Equal(1, tracker.DispatcherResolveCount);
    }

    private static IncomingEmail Mail(string receiptValue) =>
        new(
            MessageId: null,
            ReceiptHandle: new EmailReceiptHandle(EmailReceiptKind.Fake, receiptValue),
            FromAddress: "customer@example.test",
            FromDisplayName: "Customer",
            Subject: "Help",
            Body: "Body",
            IsHtml: false,
            ReceivedAt: DateTime.UtcNow,
            Attachments: Array.Empty<IncomingEmailAttachment>());

    private sealed class ScopeTracker
    {
        public List<Guid> CreatedScopeIds { get; } = [];
        public List<Guid> DisposedScopeIds { get; } = [];
        public List<string> ProcessedReceiptValues { get; } = [];
        public List<Guid> ProcessorInstanceIds { get; } = [];
        public int DispatcherResolveCount { get; set; }
    }

    private sealed class TrackingServiceScopeFactory(
        ServiceProvider root,
        ScopeTracker tracker) : IServiceScopeFactory
    {
        public IServiceScope CreateScope()
        {
            var inner = root.CreateScope();
            var id = Guid.NewGuid();
            tracker.CreatedScopeIds.Add(id);
            return new TrackedScope(inner, tracker, id);
        }

        private sealed class TrackedScope(
            IServiceScope inner,
            ScopeTracker tracker,
            Guid id) : IServiceScope, IAsyncDisposable
        {
            private int disposed;

            public IServiceProvider ServiceProvider => inner.ServiceProvider;

            public void Dispose()
            {
                if (Interlocked.Exchange(ref disposed, 1) == 0)
                {
                    tracker.DisposedScopeIds.Add(id);
                    inner.Dispose();
                }
            }

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }
        }
    }

    private sealed class TrackingProcessor(ScopeTracker tracker) : IInboundEmailItemProcessor
    {
        private readonly Guid instanceId = Guid.NewGuid();

        public Task<InboundEmailItemResult> ProcessAsync(
            IncomingEmail email,
            CancellationToken cancellationToken)
        {
            tracker.ProcessorInstanceIds.Add(instanceId);
            tracker.ProcessedReceiptValues.Add(email.ReceiptHandle.Value);
            return Task.FromResult(new InboundEmailItemResult(
                InboundEmailItemOutcome.CreatedTicket,
                IdempotencyKey: "key",
                TicketNumber: "VS-000001",
                WasReopened: false,
                AcknowledgementSent: true,
                AcknowledgementFailed: false,
                FailureCode: null));
        }
    }

    private sealed class EmptyDb : IApplicationDbContext, IProcessedEmailRepository, ITicketRepository, IUnitOfWork
    {
        public Task<ProcessedEmailMessage?> GetByIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken) => Task.FromResult<ProcessedEmailMessage?>(null);
        public Task<ProcessedEmailMessage?> GetByIdAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult<ProcessedEmailMessage?>(null);
        public Task AddAsync(ProcessedEmailMessage message, CancellationToken cancellationToken) => Task.CompletedTask;
        IQueryable<ProcessedEmailMessage> IProcessedEmailRepository.GetListQueryable() => ProcessedEmailMessages;

        Task<Ticket?> ITicketRepository.GetByIdAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult<Ticket?>(null);
        public Task<Ticket?> GetByNumberAsync(string ticketNumber, CancellationToken cancellationToken) => Task.FromResult<Ticket?>(null);
        public IQueryable<Ticket> GetListQueryable() => Tickets;
        public Task AddAsync(Ticket ticket, CancellationToken cancellationToken) => Task.CompletedTask;
        public void Update(Ticket ticket) { }
        public Task AddMessageAsync(TicketMessage message, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<bool> MessageExistsAsync(Guid messageId, CancellationToken cancellationToken) => Task.FromResult(false);
        public Task<TicketMessage?> GetMessageByIdAsync(Guid messageId, CancellationToken cancellationToken) => Task.FromResult<TicketMessage?>(null);
        public Task<Guid> GetFirstMessageIdAsync(Guid ticketId, CancellationToken cancellationToken) => Task.FromResult(Guid.Empty);

        public IQueryable<User> Users => Array.Empty<User>().AsQueryable();
        public IQueryable<Ticket> Tickets => Array.Empty<Ticket>().AsQueryable();
        public IQueryable<TicketMessage> TicketMessages => Array.Empty<TicketMessage>().AsQueryable();
        public IQueryable<TicketAttachment> TicketAttachments =>
            Array.Empty<TicketAttachment>().AsQueryable();
        public IQueryable<ProcessedEmailMessage> ProcessedEmailMessages =>
            Array.Empty<ProcessedEmailMessage>().AsQueryable();

        public IQueryable<ApplicationParameter> ApplicationParameters =>
            Array.Empty<ApplicationParameter>().AsQueryable();

        public IQueryable<ParameterChangeLog> ParameterChangeLogs =>
            Array.Empty<ParameterChangeLog>().AsQueryable();

        public IQueryable<SystemLog> SystemLogs =>
            Array.Empty<SystemLog>().AsQueryable();

        public void Add<TEntity>(TEntity entity) where TEntity : class
        {
        }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken) =>
            Task.FromResult(0);

        public void ClearTrackedChanges()
        {
        }
    }

    private sealed class NoopSender : IEmailSender
    {
        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}
