using VSHelpDesk.Application.Common.Exceptions;
using VSHelpDesk.Application.Features.Tickets.GetTicketDetails;
using VSHelpDesk.Application.Features.Tickets.ReadModel;

namespace VSHelpDesk.Application.UnitTests.Features.Tickets.GetTicketDetails;

public sealed class GetTicketDetailsHandlerTests
{
    private static readonly DateTime T0 = new(2026, 8, 3, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task HandleAsync_ExistingTicket_RequestsNewest100MessagesAndReturnsEncodedCursor()
    {
        var ticketId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var nextCursor = new TicketMessageCursor(
            T0.AddMinutes(-100),
            Guid.Parse("22222222-2222-2222-2222-222222222222"));
        var message = new TicketMessageDto(
            Guid.Parse("33333333-3333-3333-3333-333333333333"),
            "Customer",
            null,
            "Literal <strong>message</strong>",
            false,
            T0);
        var repository = new RecordingRepository(new TicketDetailsReadResult(
            CreateDetails(ticketId, [message]),
            nextCursor,
            true));
        var codec = new TicketMessageCursorCodec();
        var handler = new GetTicketDetailsHandler(repository, codec);
        using var cancellation = new CancellationTokenSource();

        var details = await handler.HandleAsync(
            new GetTicketDetailsQuery(ticketId),
            cancellation.Token);

        Assert.Equal(ticketId, repository.DetailTicketId);
        Assert.Equal(100, repository.DetailMessagePageSize);
        Assert.Equal(cancellation.Token, repository.CancellationToken);
        Assert.Equal([message], details.Messages);
        Assert.Equal("Literal <strong>message</strong>", details.Messages[0].Content);
        Assert.True(details.HasMoreMessages);
        Assert.Equal(nextCursor, codec.Decode(details.NextMessageCursor!));
    }

    [Fact]
    public async Task HandleAsync_UnknownId_ThrowsNotFoundException()
    {
        var handler = new GetTicketDetailsHandler(
            new RecordingRepository(detailResult: null),
            new TicketMessageCursorCodec());

        await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.HandleAsync(new GetTicketDetailsQuery(Guid.NewGuid()), CancellationToken.None));
    }

    private static TicketDetailsDto CreateDetails(
        Guid ticketId,
        IReadOnlyList<TicketMessageDto>? messages = null) =>
        new(
            ticketId,
            "VS-000020",
            "Subject locked",
            "Ada",
            "ada@example.test",
            "New",
            null,
            T0,
            T0,
            T0,
            null,
            null,
            null,
            messages ?? [],
            [],
            null,
            false);

    private sealed class RecordingRepository(TicketDetailsReadResult? detailResult)
        : ITicketDetailReadRepository
    {
        public Guid? DetailTicketId { get; private set; }
        public int? DetailMessagePageSize { get; private set; }
        public CancellationToken CancellationToken { get; private set; }

        public Task<TicketDetailsReadResult?> ReadDetailsAsync(
            Guid ticketId,
            int messagePageSize,
            CancellationToken cancellationToken)
        {
            DetailTicketId = ticketId;
            DetailMessagePageSize = messagePageSize;
            CancellationToken = cancellationToken;
            return Task.FromResult(detailResult);
        }

        public Task<TicketMessageReadResult?> ReadMessagesAsync(
            Guid ticketId,
            TicketMessageReadRequest request,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
