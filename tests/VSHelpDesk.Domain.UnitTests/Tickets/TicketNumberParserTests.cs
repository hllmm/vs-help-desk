using VSHelpDesk.Domain.Tickets;

namespace VSHelpDesk.Domain.UnitTests.Tickets;

public sealed class TicketNumberParserTests
{
    [Theory]
    [InlineData("Re: [VS-000042] Printer", "VS-000042")]
    [InlineData("VS-1 something", "VS-000001")]
    [InlineData("no ticket here", null)]
    [InlineData("re: [vs-000042] lower", "VS-000042")]
    [InlineData("Fwd: VS-1 and VS-2 more", "VS-000001")]
    [InlineData("CVS-000001 should not match", null)]
    [InlineData("VS-1234567 seven digits", null)]
    [InlineData("VS-0 invalid zero", null)]
    [InlineData(null, null)]
    [InlineData("", null)]
    public void TryFindInText_ExtractsCanonicalNumber(string? subject, string? expected)
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
