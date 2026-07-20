using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using VSHelpDesk.Infrastructure.Email;

namespace VSHelpDesk.Infrastructure.UnitTests.Email;

public sealed class FakeEmailReceiverTests
{
    [Fact]
    public async Task FetchUnread_ReturnsDeterministicSamples_AndMarkProcessedHidesThem()
    {
        var receiver = new FakeEmailReceiver(
            Options.Create(new EmailOptions { ReceiverMode = "Fake" }),
            NullLogger<FakeEmailReceiver>.Instance);

        var first = await receiver.FetchUnreadAsync();
        Assert.Equal(2, first.Count);
        Assert.All(first, message => Assert.False(string.IsNullOrWhiteSpace(message.MessageId)));
        Assert.All(first, message => Assert.False(string.IsNullOrWhiteSpace(message.Subject)));

        await receiver.MarkAsProcessedAsync(first[0].MessageId);
        var second = await receiver.FetchUnreadAsync();
        Assert.Single(second);
        Assert.DoesNotContain(second, message => message.MessageId == first[0].MessageId);
    }
}
