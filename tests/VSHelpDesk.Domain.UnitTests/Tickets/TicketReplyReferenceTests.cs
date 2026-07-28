using VSHelpDesk.Domain.Tickets;

namespace VSHelpDesk.Domain.UnitTests.Tickets;

public sealed class TicketReplyReferenceTests
{
    [Fact]
    public void FormatAndParse_RoundTrip()
    {
        const string token = "0123456789abcdef0123456789abcdef";

        var value = TicketReplyReference.Format("VS-000123", token);

        Assert.Equal(
            "[VS-000123:R-0123456789abcdef0123456789abcdef]",
            value);
        Assert.True(TicketReplyReference.TryFindInText(
            value,
            out var number,
            out var parsedToken));
        Assert.Equal("VS-000123", number);
        Assert.Equal(token, parsedToken);
    }

    [Theory]
    [InlineData("[VS-000123]")]
    [InlineData("[VS-000123:R-short]")]
    [InlineData("[VS-000123:R-0123456789abcdef0123456789abcdeg]")]
    public void TryFindInText_InvalidReference_ReturnsFalse(string value)
    {
        Assert.False(TicketReplyReference.TryFindInText(value, out _, out _));
    }
}
