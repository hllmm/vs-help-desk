using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MimeKit;
using VSHelpDesk.Application.Abstractions.Email;
using VSHelpDesk.Application.Features.MailProcessing;
using VSHelpDesk.Infrastructure.Email;
using VSHelpDesk.Infrastructure.Storage;

namespace VSHelpDesk.Infrastructure.UnitTests.Email;

public sealed class MailKitImapQuotaTests
{
    [Fact]
    public void MailboxQuotaOptions_defaults_match_limits()
    {
        var opts = new MailboxQuotaOptions();
        Assert.Equal(VSHelpDesk.Domain.Mail.MailboxQuota.MaxMessagesPerRun, opts.MaxMessagesPerRun);
        Assert.Equal(VSHelpDesk.Domain.Mail.MailboxQuota.MaxAttachmentsPerMessage, opts.MaxAttachmentsPerMessage);
        Assert.Equal(VSHelpDesk.Domain.Mail.MailboxQuota.MaxAggregateBytesPerRun, opts.MaxAggregateBytesPerRun);
        Assert.Equal(VSHelpDesk.Domain.Mail.MailboxQuota.MaxRawMessageBytes, opts.MaxRawMessageBytes);
        Assert.Equal(100, opts.MaxMessagesPerRun);
        Assert.Equal(10, opts.MaxAttachmentsPerMessage);
        Assert.Equal(50L * 1024 * 1024, opts.MaxAggregateBytesPerRun);
        Assert.Equal(5L * 1024 * 1024, opts.MaxRawMessageBytes);
    }

    [Fact]
    public void MailboxQuota_constants_are_correct()
    {
        Assert.Equal(100, VSHelpDesk.Domain.Mail.MailboxQuota.MaxMessagesPerRun);
        Assert.Equal(10, VSHelpDesk.Domain.Mail.MailboxQuota.MaxAttachmentsPerMessage);
        Assert.Equal(50L * 1024 * 1024, VSHelpDesk.Domain.Mail.MailboxQuota.MaxAggregateBytesPerRun);
        Assert.Equal(5L * 1024 * 1024, VSHelpDesk.Domain.Mail.MailboxQuota.MaxRawMessageBytes);
    }

    [Fact]
    public async Task ImapEmailReceiver_over_limit_messages_must_have_empty_Attachments()
    {
        var mime = BuildMessageWithManyAttachments(15);
        var client = new FakeImapMailboxClient
        {
            Items = [new ImapMailboxItem(UidValidity: 1u, Uid: 1u, Message: mime)]
        };

        var receiver = CreateReceiver(client, maxFileSizeBytes: 10 * 1024 * 1024, maxAttachmentsPerMessage: 10);
        var unread = await receiver.FetchUnreadAsync().ToListAsync();
        var item = Assert.Single(unread);

        Assert.Empty(item.Attachments);
        Assert.True(item.TotalAttachmentCount > 10, $"TotalAttachmentCount={item.TotalAttachmentCount} should be >10");
        // Normalize the actual returned IncomingEmail (with TotalAttachmentCount) — do not reconstruct
        var result = InboundEmailNormalizer.Normalize(item);
        Assert.Equal(InboundEmailPolicyOutcome.Quarantine, result.Outcome);
    }

    [Fact]
    public async Task ImapEmailReceiver_html_body_truncated_before_conversion()
    {
        var hugeHtml = "<p>" + new string('A', 2 * 1024 * 1024) + "</p>";
        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress("User", "user@example.test"));
        mime.Subject = "Huge html";
        mime.Body = new TextPart("html") { Text = hugeHtml };

        var client = new FakeImapMailboxClient
        {
            Items = [new ImapMailboxItem(UidValidity: 1u, Uid: 1u, Message: mime)]
        };

        var receiver = CreateReceiver(client);
        var unread = await receiver.FetchUnreadAsync().ToListAsync();
        var item = Assert.Single(unread);

        // Body should be bounded by InboundMailLimits.MaxBodyLength after conversion + normalization
        Assert.True(item.Body!.Length <= InboundMailLimits.MaxBodyLength);
    }

    [Fact]
    public void InboundEmailNormalizer_rejects_too_many_attachments()
    {
        var attachments = Enumerable.Range(0, 15)
            .Select(i => new IncomingEmailAttachment($"f{i}.pdf", "application/pdf", 1024, new byte[1024]))
            .ToList();

        var email = new IncomingEmail(
            MessageId: "<test@example.test>",
            ReceiptHandle: new EmailReceiptHandle(EmailReceiptKind.Fake, "fake\\quota-test"),
            FromAddress: "customer@example.test",
            FromDisplayName: "Customer",
            Subject: "Help",
            Body: "Body",
            IsHtml: false,
            ReceivedAt: DateTime.UtcNow,
            Attachments: attachments);

        var result = InboundEmailNormalizer.Normalize(email);
        Assert.Equal(InboundEmailPolicyOutcome.Quarantine, result.Outcome);
        Assert.Contains("Too many attachments", result.ProcessingNote!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InboundEmailNormalizer_allows_ten_attachments()
    {
        var attachments = Enumerable.Range(0, 10)
            .Select(i => new IncomingEmailAttachment($"f{i}.pdf", "application/pdf", 1024, new byte[1024]))
            .ToList();

        var email = new IncomingEmail(
            MessageId: "<test2@example.test>",
            ReceiptHandle: new EmailReceiptHandle(EmailReceiptKind.Fake, "fake\\quota-test2"),
            FromAddress: "customer@example.test",
            FromDisplayName: "Customer",
            Subject: "Help",
            Body: "Body",
            IsHtml: false,
            ReceivedAt: DateTime.UtcNow,
            Attachments: attachments);

        var result = InboundEmailNormalizer.Normalize(email);
        Assert.Equal(InboundEmailPolicyOutcome.Process, result.Outcome);
    }

    private static MimeMessage BuildMessageWithManyAttachments(int count)
    {
        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress("Eve", "eve@example.test"));
        mime.Subject = "Many attachments";
        mime.MessageId = "many@example.test";

        var body = new TextPart("plain") { Text = "Body" };
        var multipart = new Multipart("mixed") { body };

        for (var i = 0; i < count; i++)
        {
            var part = new MimePart("application/pdf")
            {
                Content = new MimeContent(new MemoryStream(Encoding.UTF8.GetBytes($"content-{i}"))),
                ContentDisposition = new ContentDisposition(ContentDisposition.Attachment),
                ContentTransferEncoding = ContentEncoding.Base64,
                FileName = $"f{i}.pdf"
            };
            multipart.Add(part);
        }

        mime.Body = multipart;
        return mime;
    }

    private static ImapEmailReceiver CreateReceiver(
        FakeImapMailboxClient client,
        long maxFileSizeBytes = 10 * 1024 * 1024,
        int maxAttachmentsPerMessage = 10)
    {
        var emailOptions = Options.Create(new EmailOptions
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

        var quotaOptions = Options.Create(new MailboxQuotaOptions
        {
            MaxMessagesPerRun = 100,
            MaxAttachmentsPerMessage = maxAttachmentsPerMessage,
            MaxAggregateBytesPerRun = 50 * 1024 * 1024,
            MaxRawMessageBytes = 5 * 1024 * 1024
        });

        var fileStorageOptions = Options.Create(new FileStorageOptions
        {
            RootPath = "storage",
            MaxFileSizeBytes = maxFileSizeBytes
        });

        return new ImapEmailReceiver(
            emailOptions,
            fileStorageOptions,
            client,
            new HtmlToPlainTextConverter(),
            NullLogger<ImapEmailReceiver>.Instance,
            quotaOptions);
    }

    private sealed class FakeImapMailboxClient : IImapMailboxClient
    {
        public List<ImapMailboxItem> Items { get; init; } = [];
        public List<(uint ExpectedUidValidity, uint Uid)> Marked { get; } = [];

        public async IAsyncEnumerable<ImapMailboxItem> FetchUnreadAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken) { foreach (var i in Items) { yield return i; await Task.Yield(); } }

        public Task MarkSeenAsync(uint expectedUidValidity, uint uid, CancellationToken cancellationToken)
        {
            Marked.Add((expectedUidValidity, uid));
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
