using VSHelpDesk.Domain.Tickets;

namespace VSHelpDesk.Domain.UnitTests.Tickets;

public sealed class TicketNumberParserTests
{
    [Theory]
    [InlineData("Re: [VS-000042] Printer", "VS-000042")]
    [InlineData("VS-1 something", "VS-000001")]
    [InlineData("no ticket here", null)]
    public void TryFindInText_ExtractsCanonicalNumber(string subject, string? expected)
    {
        var found = TicketNumberParser.TryFindInText(subject, out var number);
        if (expected is null)
        {
            Assert.False(found);
            return;
        }

        Assert.True(found);
        Assert.Equal(expected, number);
    }
}
