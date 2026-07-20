using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MimeKit;
using VSHelpDesk.Application.Abstractions.Email;
using VSHelpDesk.Infrastructure.Email;

namespace VSHelpDesk.Infrastructure.UnitTests.Email;

public sealed class ImapEmailReceiverTests
{
    [Fact]
    public async Task Receiver_PrefersTextBody_AndMapsNullableMessageId()
    {
        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress("Ada Lovelace", "ada@example.test"));
        mime.Subject = "Mixed body";
        mime.Date = new DateTimeOffset(2026, 7, 20, 12, 0, 0, TimeSpan.Zero);
        mime.Body = new Multipart("alternative")
        {
            new TextPart("plain") { Text = "Prefer this plain body" },
            new TextPart("html") { Text = "<p>Ignore this HTML</p>" }
        };
        // MimeKit may auto-assign Message-Id; clear so mapping stays nullable.
        mime.Headers.Remove(HeaderId.MessageId);

        var client = new FakeImapMailboxClient
        {
            Items =
            [
                new ImapMailboxItem(UidValidity: 5u, Uid: 11u, Message: mime)
            ]
        };

        var receiver = CreateReceiver(client);
        var unread = await receiver.FetchUnreadAsync();

        var item = Assert.Single(unread);
        Assert.Null(item.MessageId);
        Assert.Equal(EmailReceiptKind.Imap, item.ReceiptHandle.Kind);
        Assert.Equal(
            "imap\0test-account\0INBOX\05\011",
            item.ReceiptHandle.Value);
        Assert.Equal("ada@example.test", item.FromAddress);
        Assert.Equal("Ada Lovelace", item.FromDisplayName);
        Assert.Equal("Mixed body", item.Subject);
        Assert.Equal("Prefer this plain body", item.Body);
        Assert.False(item.IsHtml);
        Assert.Equal(new DateTime(2026, 7, 20, 12, 0, 0, DateTimeKind.Utc), item.ReceivedAt);
    }

    [Fact]
    public async Task Receiver_HtmlOnly_UsesSafePlainText()
    {
        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress("Bob", "bob@example.test"));
        mime.Subject = "HTML only";
        mime.MessageId = "html-only@example.test";
        mime.Body = new TextPart("html")
        {
            Text = "<p>Hello&nbsp;HTML</p><script>alert(1)</script>"
        };

        var client = new FakeImapMailboxClient
        {
            Items =
            [
                new ImapMailboxItem(UidValidity: 1u, Uid: 2u, Message: mime)
            ]
        };

        var receiver = CreateReceiver(client);
        var unread = await receiver.FetchUnreadAsync();

        var item = Assert.Single(unread);
        Assert.False(string.IsNullOrWhiteSpace(item.MessageId));
        Assert.Contains("Hello", item.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("alert", item.Body, StringComparison.OrdinalIgnoreCase);
        Assert.False(item.IsHtml);
    }

    [Fact]
    public async Task Receiver_MarkProcessed_UsesReceiptUidNotMessageId()
    {
        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress("Carol", "carol@example.test"));
        mime.Subject = "Mark me";
        mime.MessageId = "mark-me@example.test";
        mime.Body = new TextPart("plain") { Text = "Body" };

        var client = new FakeImapMailboxClient
        {
            Items =
            [
                new ImapMailboxItem(UidValidity: 99u, Uid: 7u, Message: mime)
            ]
        };

        var receiver = CreateReceiver(client);
        var unread = await receiver.FetchUnreadAsync();
        var item = Assert.Single(unread);

        await receiver.MarkAsProcessedAsync(item.ReceiptHandle);

        var marked = Assert.Single(client.Marked);
        Assert.Equal(99u, marked.ExpectedUidValidity);
        Assert.Equal(7u, marked.Uid);
        Assert.DoesNotContain("mark-me@example.test", item.ReceiptHandle.Value, StringComparison.Ordinal);
    }

    private static ImapEmailReceiver CreateReceiver(FakeImapMailboxClient client)
    {
        var options = Options.Create(new EmailOptions
        {
            ReceiverMode = "Imap",
            ImapHost = "localhost",
            ImapPort = 3143,
            ImapSecurityMode = MailTransportSecurityMode.None,
            ImapUsername = "support@vshelpdesk.test",
            ImapPassword = "test",
            ImapAccountId = "test-account",
            ImapFolder = "INBOX",
            SmtpHost = "localhost",
            SmtpPort = 3025,
            SupportMailboxAddress = "support@vshelpdesk.test"
        });

        return new ImapEmailReceiver(
            options,
            client,
            new HtmlToPlainTextConverter(),
            NullLogger<ImapEmailReceiver>.Instance);
    }

    private sealed class FakeImapMailboxClient : IImapMailboxClient
    {
        public List<ImapMailboxItem> Items { get; init; } = [];

        public List<(uint ExpectedUidValidity, uint Uid)> Marked { get; } = [];

        public Task<IReadOnlyList<ImapMailboxItem>> FetchUnreadAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ImapMailboxItem>>(Items);

        public Task MarkSeenAsync(
            uint expectedUidValidity,
            uint uid,
            CancellationToken cancellationToken)
        {
            Marked.Add((expectedUidValidity, uid));
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
