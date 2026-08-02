using VSHelpDesk.Application.Common.Exceptions;
using VSHelpDesk.Application.Features.Tickets.GetTicketDetails;
using VSHelpDesk.Application.Features.Tickets.GetTicketMessages;
using VSHelpDesk.Application.Features.Tickets.ReadModel;

namespace VSHelpDesk.Application.UnitTests.Features.Tickets.GetTicketMessages;

public sealed class GetTicketMessagesHandlerTests
{
    private static readonly DateTime T0 = new(2026, 8, 3, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task HandleAsync_DefaultQuery_UsesDefaultPageSizeAndUnchangedTicketId()
    {
        var repository = new RecordingRepository();
        var handler = new GetTicketMessagesHandler(repository, new TicketMessageCursorCodec());
        var ticketId = Guid.Parse("11111111-1111-1111-1111-111111111111");

        await handler.HandleAsync(new GetTicketMessagesQuery(ticketId), CancellationToken.None);

        Assert.Equal(ticketId, repository.TicketId);
        Assert.Equal(100, repository.Request!.PageSize);
        Assert.Null(repository.Request.Cursor);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(201, 200)]
    public async Task HandleAsync_OutOfRangePageSize_ClampsToMessageBounds(
        int pageSize,
        int expectedPageSize)
    {
        var repository = new RecordingRepository();
        var handler = new GetTicketMessagesHandler(repository, new TicketMessageCursorCodec());

        await handler.HandleAsync(
            new GetTicketMessagesQuery(Guid.NewGuid(), pageSize),
            CancellationToken.None);

        Assert.Equal(expectedPageSize, repository.Request!.PageSize);
    }

    [Fact]
    public async Task HandleAsync_ValidCursor_DecodesAndForwardsCursorAndCancellation()
    {
        var repository = new RecordingRepository();
        var codec = new TicketMessageCursorCodec();
        var expectedCursor = new TicketMessageCursor(
            T0,
            Guid.Parse("22222222-2222-2222-2222-222222222222"));
        var handler = new GetTicketMessagesHandler(repository, codec);
        using var cancellation = new CancellationTokenSource();

        await handler.HandleAsync(
            new GetTicketMessagesQuery(Guid.NewGuid(), Cursor: codec.Encode(expectedCursor)),
            cancellation.Token);

        Assert.Equal(expectedCursor, repository.Request!.Cursor);
        Assert.Equal(cancellation.Token, repository.CancellationToken);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task HandleAsync_ExplicitEmptyOrWhitespaceCursor_ThrowsStableValidationCode(
        string cursor)
    {
        var repository = new RecordingRepository();
        var handler = new GetTicketMessagesHandler(repository, new TicketMessageCursorCodec());

        var exception = await Assert.ThrowsAsync<RequestValidationException>(() =>
            handler.HandleAsync(
                new GetTicketMessagesQuery(Guid.NewGuid(), Cursor: cursor),
                CancellationToken.None));

        Assert.Equal("invalid-ticket-message-cursor", exception.Code);
        Assert.Null(repository.Request);
    }

    [Fact]
    public async Task HandleAsync_RepositoryResult_ReturnsPageAndEncodedNextCursor()
    {
        var nextCursor = new TicketMessageCursor(
            T0.AddMinutes(-1),
            Guid.Parse("33333333-3333-3333-3333-333333333333"));
        var message = new TicketMessageDto(
            Guid.Parse("44444444-4444-4444-4444-444444444444"),
            "Support",
            null,
            "Reply",
            false,
            T0);
        var attachment = new TicketAttachmentMetaDto(
            Guid.Parse("55555555-5555-5555-5555-555555555555"),
            message.Id,
            "evidence.txt",
            "text/plain",
            42,
            T0);
        var repository = new RecordingRepository(new TicketMessageReadResult(
            [message],
            [attachment],
            nextCursor,
            true));
        var codec = new TicketMessageCursorCodec();
        var handler = new GetTicketMessagesHandler(repository, codec);

        var page = await handler.HandleAsync(
            new GetTicketMessagesQuery(Guid.NewGuid()),
            CancellationToken.None);

        Assert.Equal([message], page.Messages);
        Assert.Equal([attachment], page.Attachments);
        Assert.True(page.HasMore);
        Assert.Equal(nextCursor, codec.Decode(page.NextCursor!));
    }

    [Fact]
    public async Task HandleAsync_UnknownTicket_ThrowsNotFoundException()
    {
        var handler = new GetTicketMessagesHandler(
            new RecordingRepository(result: null),
            new TicketMessageCursorCodec());

        await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.HandleAsync(
                new GetTicketMessagesQuery(Guid.NewGuid()),
                CancellationToken.None));
    }

    private sealed class RecordingRepository : ITicketDetailReadRepository
    {
        private readonly TicketMessageReadResult? result;

        public RecordingRepository()
            : this(new TicketMessageReadResult([], [], null, false))
        {
        }

        public RecordingRepository(TicketMessageReadResult? result)
        {
            this.result = result;
        }

        public Guid? TicketId { get; private set; }
        public TicketMessageReadRequest? Request { get; private set; }
        public CancellationToken CancellationToken { get; private set; }

        public Task<TicketDetailsReadResult?> ReadDetailsAsync(
            Guid ticketId,
            int messagePageSize,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<TicketMessageReadResult?> ReadMessagesAsync(
            Guid ticketId,
            TicketMessageReadRequest request,
            CancellationToken cancellationToken)
        {
            TicketId = ticketId;
            Request = request;
            CancellationToken = cancellationToken;
            return Task.FromResult(result);
        }
    }
}
