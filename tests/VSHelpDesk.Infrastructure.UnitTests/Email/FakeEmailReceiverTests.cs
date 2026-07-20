using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using VSHelpDesk.Application.Abstractions.Email;
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
        Assert.All(first, message => Assert.NotNull(message.ReceiptHandle));
        Assert.All(first, message => Assert.Equal(EmailReceiptKind.Fake, message.ReceiptHandle.Kind));
        Assert.All(first, message => Assert.False(string.IsNullOrWhiteSpace(message.ReceiptHandle.Value)));
        Assert.All(first, message => Assert.False(string.IsNullOrWhiteSpace(message.Subject)));

        await receiver.MarkAsProcessedAsync(first[0].ReceiptHandle);
        var second = await receiver.FetchUnreadAsync();
        Assert.Single(second);
        Assert.DoesNotContain(
            second,
            message => message.ReceiptHandle.Value == first[0].ReceiptHandle.Value);
    }

    [Fact]
    public async Task MarkAsProcessed_UsesReceiptHandle_EvenWhenMessageIdIsNull()
    {
        var receiver = new FakeEmailReceiver(
            Options.Create(new EmailOptions { ReceiverMode = "Fake" }),
            NullLogger<FakeEmailReceiver>.Instance);

        var unread = await receiver.FetchUnreadAsync();
        var first = unread[0];
        var nullMessageIdReceipt = first.ReceiptHandle;

        await receiver.MarkAsProcessedAsync(nullMessageIdReceipt);

        var remaining = await receiver.FetchUnreadAsync();
        Assert.DoesNotContain(
            remaining,
            message => message.ReceiptHandle.Value == nullMessageIdReceipt.Value);
    }
}
