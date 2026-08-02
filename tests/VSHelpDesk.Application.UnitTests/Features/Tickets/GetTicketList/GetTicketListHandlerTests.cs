using VSHelpDesk.Application.Common.Exceptions;
using VSHelpDesk.Application.Features.Tickets.GetTicketList;
using VSHelpDesk.Application.Features.Tickets.ReadModel;
using VSHelpDesk.Domain.Enums;

namespace VSHelpDesk.Application.UnitTests.Features.Tickets.GetTicketList;

public sealed class GetTicketListHandlerTests
{
    private static readonly DateTime T0 = new(2026, 8, 3, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task HandleAsync_DefaultQuery_UsesDefaultPageSize()
    {
        var repository = new RecordingRepository();
        var handler = new GetTicketListHandler(repository, new TicketListCursorCodec());

        await handler.HandleAsync(new GetTicketListQuery(), CancellationToken.None);

        Assert.Equal(50, repository.Request!.PageSize);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(101, 100)]
    public async Task HandleAsync_OutOfRangePageSize_ClampsToListBounds(int pageSize, int expectedPageSize)
    {
        var repository = new RecordingRepository();
        var handler = new GetTicketListHandler(repository, new TicketListCursorCodec());

        await handler.HandleAsync(new GetTicketListQuery(PageSize: pageSize), CancellationToken.None);

        Assert.Equal(expectedPageSize, repository.Request!.PageSize);
    }

    [Fact]
    public async Task HandleAsync_WhitespaceSearch_ForwardsNullSearch()
    {
        var repository = new RecordingRepository();
        var handler = new GetTicketListHandler(repository, new TicketListCursorCodec());

        await handler.HandleAsync(new GetTicketListQuery(Search: "  \t  "), CancellationToken.None);

        Assert.Null(repository.Request!.Search);
    }

    [Theory]
    [InlineData("a", "ticket-search-too-short")]
    [InlineData("xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx", "ticket-search-too-long")]
    public async Task HandleAsync_InvalidSearch_ThrowsStableValidationCode(string search, string expectedCode)
    {
        var handler = new GetTicketListHandler(new RecordingRepository(), new TicketListCursorCodec());

        var exception = await Assert.ThrowsAsync<RequestValidationException>(
            () => handler.HandleAsync(new GetTicketListQuery(Search: search), CancellationToken.None));

        Assert.Equal(expectedCode, exception.Code);
    }

    [Fact]
    public async Task HandleAsync_ValidCursor_DecodesAndForwardsItToRepository()
    {
        var repository = new RecordingRepository();
        var codec = new TicketListCursorCodec();
        var expectedCursor = new TicketListCursor(T0, "VS-000123");
        var handler = new GetTicketListHandler(repository, codec);

        await handler.HandleAsync(
            new GetTicketListQuery(Status: TicketStatus.Resolved, Cursor: codec.Encode(expectedCursor)),
            CancellationToken.None);

        Assert.Equal(TicketStatus.Resolved, repository.Request!.Status);
        Assert.Equal(expectedCursor, repository.Request.Cursor);
    }

    [Fact]
    public async Task HandleAsync_RepositoryResult_ReturnsItemsCountsAndEncodedNextCursor()
    {
        var nextCursor = new TicketListCursor(T0.AddMinutes(-1), "VS-000122");
        var expectedItem = new TicketListItemDto(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            "VS-000123",
            "Cannot sign in",
            "Ada Lovelace",
            "ada@example.com",
            nameof(TicketStatus.CustomerReplied),
            T0,
            null);
        var expectedCounts = new TicketStatusCountsDto(10, 2, 3, 4, 1);
        var repository = new RecordingRepository(
            new TicketListReadResult([expectedItem], nextCursor, true, expectedCounts));
        var codec = new TicketListCursorCodec();
        var handler = new GetTicketListHandler(repository, codec);

        var result = await handler.HandleAsync(new GetTicketListQuery(), CancellationToken.None);

        Assert.Equal([expectedItem], result.Items);
        Assert.True(result.HasMore);
        Assert.Equal(expectedCounts, result.Counts);
        Assert.Equal(nextCursor, codec.Decode(result.NextCursor!));
    }

    private sealed class RecordingRepository(TicketListReadResult? result = null) : ITicketListReadRepository
    {
        public TicketListReadRequest? Request { get; private set; }

        public Task<TicketListReadResult> ReadAsync(
            TicketListReadRequest request,
            CancellationToken cancellationToken)
        {
            Request = request;
            return Task.FromResult(result ?? new TicketListReadResult(
                [],
                null,
                false,
                new TicketStatusCountsDto(0, 0, 0, 0, 0)));
        }
    }
}
