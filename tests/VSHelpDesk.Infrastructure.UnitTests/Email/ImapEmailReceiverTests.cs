using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MimeKit;
using VSHelpDesk.Application.Abstractions.Email;
using VSHelpDesk.Application.Features.MailProcessing;
using VSHelpDesk.Infrastructure.Email;
using VSHelpDesk.Infrastructure.Storage;

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
        var unread = await receiver.FetchUnreadAsync().ToListAsync();

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
        // MimeKit stores/returns MessageId without angle brackets.
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
        var unread = await receiver.FetchUnreadAsync().ToListAsync();

        var item = Assert.Single(unread);
        // Boundary canonicalizes bare MimeKit id into <left@right> for identity.
        Assert.Equal("<html-only@example.test>", item.MessageId);
        Assert.Contains("Hello", item.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("alert", item.Body, StringComparison.OrdinalIgnoreCase);
        Assert.False(item.IsHtml);
    }

    [Fact]
    public async Task Receiver_BareMimeKitMessageId_YieldsMessageIdIdempotencyKey()
    {
        // Regression: MimeKit MessageId is bare (no <>). Without boundary wrap,
        // InboundEmailIdentityFactory rejects it and falls back to receipt:imap:{sha256}.
        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress("Dana", "dana@example.test"));
        mime.Subject = "Idempotency";
        mime.MessageId = "html-only@example.test";
        Assert.Equal("html-only@example.test", mime.MessageId);
        mime.Body = new TextPart("plain") { Text = "Body" };

        var client = new FakeImapMailboxClient
        {
            Items =
            [
                new ImapMailboxItem(UidValidity: 3u, Uid: 9u, Message: mime)
            ]
        };

        var receiver = CreateReceiver(client);
        var item = Assert.Single(await receiver.FetchUnreadAsync().ToListAsync());

        Assert.Equal("<html-only@example.test>", item.MessageId);

        var identity = InboundEmailIdentityFactory.Create(item);
        Assert.Equal("<html-only@example.test>", identity.IdempotencyKey);
        Assert.Equal("<html-only@example.test>", identity.SourceMessageId);
        Assert.DoesNotContain("receipt:imap:", identity.IdempotencyKey, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData("html-only@example.test", "<html-only@example.test>")]
    [InlineData("<already@bracketed.test>", "<already@bracketed.test>")]
    [InlineData("  bare@example.test  ", "<bare@example.test>")]
    [InlineData("not-an-id", "not-an-id")]
    [InlineData("two@@example.test", "two@@example.test")]
    [InlineData("@only-right", "@only-right")]
    [InlineData("only-left@", "only-left@")]
    public void CanonicalizeMimeKitMessageId_WrapsBareValidTokensOnly(
        string? input,
        string? expected)
    {
        Assert.Equal(expected, ImapEmailReceiver.CanonicalizeMimeKitMessageId(input));
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
        var unread = await receiver.FetchUnreadAsync().ToListAsync();
        var item = Assert.Single(unread);

        await receiver.MarkAsProcessedAsync(item.ReceiptHandle);

        var marked = Assert.Single(client.Marked);
        Assert.Equal(99u, marked.ExpectedUidValidity);
        Assert.Equal(7u, marked.Uid);
        Assert.Equal("<mark-me@example.test>", item.MessageId);
        Assert.DoesNotContain("mark-me@example.test", item.ReceiptHandle.Value, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Receiver_MapsAttachmentBytes_WithinSizeCap()
    {
        var payload = Encoding.UTF8.GetBytes("hello-attachment");
        var mime = BuildMessageWithAttachment(
            fileName: "note.txt",
            contentType: "text/plain",
            content: payload);

        var client = new FakeImapMailboxClient
        {
            Items = [new ImapMailboxItem(UidValidity: 1u, Uid: 1u, Message: mime)]
        };

        var receiver = CreateReceiver(client, maxFileSizeBytes: 1024);
        var item = Assert.Single(await receiver.FetchUnreadAsync().ToListAsync());
        var attachment = Assert.Single(item.Attachments);

        Assert.Equal("note.txt", attachment.FileName);
        Assert.Equal("text/plain", attachment.ContentType);
        Assert.Equal(payload.Length, attachment.FileSize);
        Assert.Equal(payload, attachment.Content);
    }

    [Fact]
    public async Task Receiver_OmitsAttachment_WhenKnownSizeExceedsCap()
    {
        var payload = Encoding.UTF8.GetBytes("too-large-payload-for-cap");
        var mime = BuildMessageWithAttachment(
            fileName: "big.txt",
            contentType: "text/plain",
            content: payload);

        var client = new FakeImapMailboxClient
        {
            Items = [new ImapMailboxItem(UidValidity: 1u, Uid: 2u, Message: mime)]
        };

        var receiver = CreateReceiver(client, maxFileSizeBytes: 4);
        var item = Assert.Single(await receiver.FetchUnreadAsync().ToListAsync());

        Assert.Empty(item.Attachments);
    }

    private static MimeMessage BuildMessageWithAttachment(
        string fileName,
        string contentType,
        byte[] content)
    {
        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress("Eve", "eve@example.test"));
        mime.Subject = "With attachment";
        mime.MessageId = "with-attachment@example.test";

        var body = new TextPart("plain") { Text = "Body with attachment" };
        var attachment = new MimePart(contentType)
        {
            Content = new MimeContent(new MemoryStream(content)),
            ContentDisposition = new ContentDisposition(ContentDisposition.Attachment),
            ContentTransferEncoding = ContentEncoding.Base64,
            FileName = fileName
        };

        mime.Body = new Multipart("mixed") { body, attachment };
        return mime;
    }

    private static ImapEmailReceiver CreateReceiver(
        FakeImapMailboxClient client,
        long maxFileSizeBytes = 10 * 1024 * 1024)
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

        var fileStorageOptions = Options.Create(new FileStorageOptions
        {
            RootPath = "storage",
            MaxFileSizeBytes = maxFileSizeBytes
        });

        return new ImapEmailReceiver(
            options,
            fileStorageOptions,
            client,
            new HtmlToPlainTextConverter(),
            NullLogger<ImapEmailReceiver>.Instance);
    }

    private sealed class FakeImapMailboxClient : IImapMailboxClient
    {
        public List<ImapMailboxItem> Items { get; init; } = [];

        public List<(uint ExpectedUidValidity, uint Uid)> Marked { get; } = [];

        public async IAsyncEnumerable<ImapMailboxItem> FetchUnreadAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken) { foreach(var i in Items){ yield return i; await Task.Yield(); } }

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
