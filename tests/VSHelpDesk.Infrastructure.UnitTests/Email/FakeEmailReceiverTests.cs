using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using VSHelpDesk.Application.Abstractions.Email;
using VSHelpDesk.Infrastructure.Email;

namespace VSHelpDesk.Infrastructure.UnitTests.Email;

public sealed class FakeEmailReceiverTests
{
    [Fact]
    public async Task FetchUnread_DefaultRuntimeInbox_IsEmpty()
    {
        var receiver = new FakeEmailReceiver(
            Options.Create(new EmailOptions { ReceiverMode = "Fake" }),
            NullLogger<FakeEmailReceiver>.Instance);

        var unread = await receiver.FetchUnreadAsync();

        Assert.Empty(unread);
    }

    [Fact]
    public async Task FetchUnread_ReturnsDeterministicSamples_AndMarkProcessedHidesThem()
    {
        var receiver = CreateReceiverWithFixtures();

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
    public async Task FetchUnread_IncludesInjectedTextAttachmentOnFirstMessage()
    {
        var receiver = CreateReceiverWithFixtures();

        var unread = await receiver.FetchUnreadAsync();
        var first = Assert.Single(unread, m => m.MessageId == "<fixture-001@example.test>");
        var attachment = Assert.Single(first.Attachments);

        Assert.Equal("note.txt", attachment.FileName);
        Assert.Equal("text/plain", attachment.ContentType);
        Assert.Equal(Encoding.UTF8.GetBytes("test-attachment"), attachment.Content);
        Assert.Equal(attachment.Content.Length, attachment.FileSize);

        var second = Assert.Single(unread, m => m.MessageId == "<fixture-002@example.test>");
        Assert.Empty(second.Attachments);
    }

    [Fact]
    public async Task MarkAsProcessed_UsesReceiptHandle_EvenWhenMessageIdIsNull()
    {
        var receiver = CreateReceiverWithFixtures();

        var unread = await receiver.FetchUnreadAsync();
        var first = unread[0];
        var nullMessageIdReceipt = first.ReceiptHandle;

        await receiver.MarkAsProcessedAsync(nullMessageIdReceipt);

        var remaining = await receiver.FetchUnreadAsync();
        Assert.DoesNotContain(
            remaining,
            message => message.ReceiptHandle.Value == nullMessageIdReceipt.Value);
    }

    private static FakeEmailReceiver CreateReceiverWithFixtures()
    {
        var attachmentBytes = Encoding.UTF8.GetBytes("test-attachment");
        IncomingEmail[] fixtures =
        [
            new(
                MessageId: "<fixture-001@example.test>",
                ReceiptHandle: new EmailReceiptHandle(
                    EmailReceiptKind.Fake,
                    "fixture\0fixture-001"),
                FromAddress: "sender.one@example.test",
                FromDisplayName: "Fixture Sender One",
                Subject: "Fixture subject one",
                Body: "Fixture body one",
                IsHtml: false,
                ReceivedAt: DateTime.UtcNow.AddMinutes(-15),
                Attachments:
                [
                    new IncomingEmailAttachment(
                        FileName: "note.txt",
                        ContentType: "text/plain",
                        FileSize: attachmentBytes.Length,
                        Content: attachmentBytes)
                ]),
            new(
                MessageId: "<fixture-002@example.test>",
                ReceiptHandle: new EmailReceiptHandle(
                    EmailReceiptKind.Fake,
                    "fixture\0fixture-002"),
                FromAddress: "sender.two@example.test",
                FromDisplayName: "Fixture Sender Two",
                Subject: "Fixture subject two",
                Body: "Fixture body two",
                IsHtml: false,
                ReceivedAt: DateTime.UtcNow.AddMinutes(-5),
                Attachments: Array.Empty<IncomingEmailAttachment>())
        ];

        return new FakeEmailReceiver(
            Options.Create(new EmailOptions { ReceiverMode = "Fake" }),
            NullLogger<FakeEmailReceiver>.Instance,
            fixtures);
    }
}
