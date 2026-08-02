using System.Text;
using VSHelpDesk.Application.Common.Exceptions;
using VSHelpDesk.Application.Features.Tickets.ReadModel;

namespace VSHelpDesk.Application.UnitTests.Features.Tickets.GetTicketList;

public sealed class TicketListCursorCodecTests
{
    private const string InvalidCursorCode = "invalid-ticket-list-cursor";
    private readonly TicketListCursorCodec codec = new();

    [Fact]
    public void EncodeDecode_UtcCursor_RoundTripsAsBase64Url()
    {
        var cursor = new TicketListCursor(
            new DateTime(2026, 8, 3, 9, 15, 0, DateTimeKind.Utc),
            "VS-000123");

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
    public void Decode_MissingTicketNumber_ThrowsStableValidationCode()
    {
        var exception = Assert.Throws<RequestValidationException>(
            () => codec.Decode(Base64Url("{\"lastActivityAt\":\"2026-08-03T09:15:00Z\"}")));

        Assert.Equal(InvalidCursorCode, exception.Code);
    }

    [Fact]
    public void Decode_NonUtcTimestamp_ThrowsStableValidationCode()
    {
        var exception = Assert.Throws<RequestValidationException>(
            () => codec.Decode(Base64Url("{\"lastActivityAt\":\"2026-08-03T09:15:00\",\"ticketNumber\":\"VS-000123\"}")));

        Assert.Equal(InvalidCursorCode, exception.Code);
    }

    private static string Base64Url(string payload) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(payload))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
}
