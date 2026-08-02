using System.Text;
using VSHelpDesk.Application.Common.Exceptions;
using VSHelpDesk.Application.Features.Tickets.ReadModel;

namespace VSHelpDesk.Application.UnitTests.Features.Tickets.GetTicketDetails;

public sealed class TicketMessageCursorCodecTests
{
    private const string InvalidCursorCode = "invalid-ticket-message-cursor";
    private readonly TicketMessageCursorCodec codec = new();

    [Fact]
    public void EncodeDecode_UtcCursor_RoundTripsAsBase64Url()
    {
        var cursor = new TicketMessageCursor(
            new DateTime(2026, 8, 3, 9, 15, 0, DateTimeKind.Utc),
            Guid.Parse("11111111-1111-1111-1111-111111111111"));

        var encoded = codec.Encode(cursor);
        var decoded = codec.Decode(encoded);

        Assert.DoesNotContain('+', encoded);
        Assert.DoesNotContain('/', encoded);
        Assert.DoesNotContain('=', encoded);
        Assert.Equal(cursor, decoded);
    }

    [Fact]
    public void Decode_CursorLongerThan512Characters_ThrowsStableValidationCode()
    {
        var exception = Assert.Throws<RequestValidationException>(
            () => codec.Decode(new string('a', 513)));

        Assert.Equal(InvalidCursorCode, exception.Code);
    }

    [Theory]
    [InlineData("not-base64!")]
    [InlineData("a")]
    public void Decode_MalformedBase64Url_ThrowsStableValidationCode(string cursor)
    {
        var exception = Assert.Throws<RequestValidationException>(() => codec.Decode(cursor));

        Assert.Equal(InvalidCursorCode, exception.Code);
    }

    [Fact]
    public void Decode_InvalidJson_ThrowsStableValidationCode()
    {
        var exception = Assert.Throws<RequestValidationException>(
            () => codec.Decode(Base64Url("not json")));

        Assert.Equal(InvalidCursorCode, exception.Code);
    }

    [Fact]
    public void Decode_EmptyId_ThrowsStableValidationCode()
    {
        var exception = Assert.Throws<RequestValidationException>(
            () => codec.Decode(Base64Url(
                "{\"createdAt\":\"2026-08-03T09:15:00Z\",\"id\":\"00000000-0000-0000-0000-000000000000\"}")));

        Assert.Equal(InvalidCursorCode, exception.Code);
    }

    [Fact]
    public void Decode_MissingId_ThrowsStableValidationCode()
    {
        var exception = Assert.Throws<RequestValidationException>(
            () => codec.Decode(Base64Url("{\"createdAt\":\"2026-08-03T09:15:00Z\"}")));

        Assert.Equal(InvalidCursorCode, exception.Code);
    }

    [Fact]
    public void Decode_UnexpectedJsonMember_ThrowsStableValidationCode()
    {
        var exception = Assert.Throws<RequestValidationException>(
            () => codec.Decode(Base64Url(
                "{\"createdAt\":\"2026-08-03T09:15:00Z\",\"id\":\"11111111-1111-1111-1111-111111111111\",\"extra\":\"value\"}")));

        Assert.Equal(InvalidCursorCode, exception.Code);
    }

    [Fact]
    public void Decode_NonUtcTimestamp_ThrowsStableValidationCode()
    {
        var exception = Assert.Throws<RequestValidationException>(
            () => codec.Decode(Base64Url(
                "{\"createdAt\":\"2026-08-03T09:15:00\",\"id\":\"11111111-1111-1111-1111-111111111111\"}")));

        Assert.Equal(InvalidCursorCode, exception.Code);
    }

    [Fact]
    public void Encode_EmptyId_ThrowsStableValidationCode()
    {
        var exception = Assert.Throws<RequestValidationException>(() => codec.Encode(
            new TicketMessageCursor(
                new DateTime(2026, 8, 3, 9, 15, 0, DateTimeKind.Utc),
                Guid.Empty)));

        Assert.Equal(InvalidCursorCode, exception.Code);
    }

    [Fact]
    public void Encode_NonUtcTimestamp_ThrowsStableValidationCode()
    {
        var exception = Assert.Throws<RequestValidationException>(() => codec.Encode(
            new TicketMessageCursor(
                new DateTime(2026, 8, 3, 9, 15, 0, DateTimeKind.Unspecified),
                Guid.Parse("11111111-1111-1111-1111-111111111111"))));

        Assert.Equal(InvalidCursorCode, exception.Code);
    }

    private static string Base64Url(string payload) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(payload))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
}
