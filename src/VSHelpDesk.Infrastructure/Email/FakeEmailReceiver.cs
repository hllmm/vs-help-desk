using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VSHelpDesk.Application.Abstractions.Email;

namespace VSHelpDesk.Infrastructure.Email;

/// <summary>
/// Deterministic unread inbox for Development/tests when real IMAP is unavailable.
/// </summary>
public sealed class FakeEmailReceiver(
    IOptions<EmailOptions> emailOptions,
    ILogger<FakeEmailReceiver> logger) : IEmailReceiver
{
    private static readonly byte[] NoteAttachmentBytes = Encoding.UTF8.GetBytes("fake-attachment");

    private readonly List<IncomingEmail> unread =
    [
        new(
            MessageId: "<fake-unread-001@vshelpdesk.local>",
            ReceiptHandle: new EmailReceiptHandle(EmailReceiptKind.Fake, "fake\0fake-unread-001"),
            FromAddress: "customer.one@example.test",
            FromDisplayName: "Customer One",
            Subject: "Printer offline in office A",
            Body: "Hello, our office printer stopped working this morning.",
            IsHtml: false,
            ReceivedAt: DateTime.UtcNow.AddMinutes(-15),
            Attachments:
            [
                new IncomingEmailAttachment(
                    FileName: "note.txt",
                    ContentType: "text/plain",
                    FileSize: NoteAttachmentBytes.Length,
                    Content: NoteAttachmentBytes)
            ]),
        new(
            MessageId: "<fake-unread-002@vshelpdesk.local>",
            ReceiptHandle: new EmailReceiptHandle(EmailReceiptKind.Fake, "fake\0fake-unread-002"),
            FromAddress: "customer.two@example.test",
            FromDisplayName: "Customer Two",
            Subject: "VPN access request",
            Body: "Please enable VPN for the new contractor.",
            IsHtml: false,
            ReceivedAt: DateTime.UtcNow.AddMinutes(-5),
            Attachments: Array.Empty<IncomingEmailAttachment>())
    ];

    private readonly HashSet<string> processedIds = new(StringComparer.Ordinal);

    public Task<IReadOnlyList<IncomingEmail>> FetchUnreadAsync(CancellationToken cancellationToken = default)
    {
        _ = emailOptions.Value;
        var batch = unread
            .Where(message => !processedIds.Contains(message.ReceiptHandle.Value))
            .ToList();

        logger.LogInformation(
            "Fake email receiver fetched unread count={Count} mode=Fake",
            batch.Count);

        return Task.FromResult<IReadOnlyList<IncomingEmail>>(batch);
    }

    public Task MarkAsProcessedAsync(
        EmailReceiptHandle receiptHandle,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(receiptHandle);

        if (!string.IsNullOrWhiteSpace(receiptHandle.Value))
        {
            processedIds.Add(receiptHandle.Value);
        }

        logger.LogInformation(
            "Fake email receiver marked message processed receiptKind={ReceiptKind}",
            receiptHandle.Kind);

        return Task.CompletedTask;
    }
}
