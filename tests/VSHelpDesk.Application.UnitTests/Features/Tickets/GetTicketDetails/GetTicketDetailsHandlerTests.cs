using VSHelpDesk.Application.Abstractions.Persistence;
using VSHelpDesk.Application.Common.Exceptions;
using VSHelpDesk.Application.Features.Tickets.GetTicketDetails;
using VSHelpDesk.Domain.Entities;
using VSHelpDesk.Domain.Enums;

namespace VSHelpDesk.Application.UnitTests.Features.Tickets.GetTicketDetails;

public sealed class GetTicketDetailsHandlerTests
{
    private static readonly DateTime T0 = new(2026, 8, 3, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task UC004_ReturnsTicketWithMessagesInChronologicalOrder()
    {
        var ticket = Ticket.Create("VS-000020", "Subject locked", "Ada", "ada@t.com", T0);
        var later = new TicketMessage(
            ticket.Id,
            MessageSenderType.Support,
            "Second",
            createdAtUtc: T0.AddMinutes(10));
        var earlier = new TicketMessage(
            ticket.Id,
            MessageSenderType.Customer,
            "First",
            createdAtUtc: T0.AddMinutes(1));
        var handler = new GetTicketDetailsHandler(new FakeDb(ticket, earlier, later));

        var details = await handler.HandleAsync(new GetTicketDetailsQuery(ticket.Id), CancellationToken.None);

        Assert.Equal(ticket.Id, details.Id);
        Assert.Equal("Subject locked", details.Subject);
        Assert.Equal(2, details.Messages.Count);
        Assert.Equal("First", details.Messages[0].Content);
        Assert.Equal("Second", details.Messages[1].Content);
        Assert.Empty(details.Attachments);
    }

    [Fact]
    public async Task UC004_UnknownId_ThrowsNotFoundException()
    {
        var handler = new GetTicketDetailsHandler(new FakeDb());

        await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.HandleAsync(new GetTicketDetailsQuery(Guid.NewGuid()), CancellationToken.None));
    }

    private sealed class FakeDb : IApplicationDbContext
    {
        private readonly List<Ticket> tickets;
        private readonly List<TicketMessage> messages;
        private readonly List<TicketAttachment> attachments;

        public FakeDb(
            Ticket? ticket = null,
            TicketMessage[]? ticketMessages = null,
            TicketAttachment[]? ticketAttachments = null)
        {
            tickets = ticket is null ? [] : [ticket];
            messages = ticketMessages?.ToList() ?? [];
            attachments = ticketAttachments?.ToList() ?? [];
        }

        public FakeDb(Ticket ticket, params TicketMessage[] ticketMessages)
            : this(ticket, ticketMessages, null)
        {
        }

        public IQueryable<User> Users => Array.Empty<User>().AsQueryable();
        public IQueryable<Ticket> Tickets => tickets.AsQueryable();
        public IQueryable<TicketMessage> TicketMessages => messages.AsQueryable();
        public IQueryable<TicketAttachment> TicketAttachments => attachments.AsQueryable();
        public IQueryable<ProcessedEmailMessage> ProcessedEmailMessages =>
            Array.Empty<ProcessedEmailMessage>().AsQueryable();

        public IQueryable<ApplicationParameter> ApplicationParameters =>
            Array.Empty<ApplicationParameter>().AsQueryable();

        public IQueryable<ParameterChangeLog> ParameterChangeLogs =>
            Array.Empty<ParameterChangeLog>().AsQueryable();

        public void Add<TEntity>(TEntity entity) where TEntity : class
        {
        }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(0);

        public void ClearTrackedChanges()
        {
        }

    }
}
