using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using VSHelpDesk.Application.Abstractions.Email;

namespace VSHelpDesk.Infrastructure.Email;

/// <summary>
/// Operational IMAP <see cref="IEmailReceiver"/>: maps MIME to boundary DTO and marks Seen by receipt.
/// </summary>
public sealed class ImapEmailReceiver(
    IOptions<EmailOptions> emailOptions,
    IImapMailboxClient mailboxClient,
    HtmlToPlainTextConverter htmlConverter,
    ILogger<ImapEmailReceiver> logger) : IEmailReceiver
{
    public async Task<IReadOnlyList<IncomingEmail>> FetchUnreadAsync(
        CancellationToken cancellationToken = default)
    {
        var options = emailOptions.Value;
        var accountId = options.ImapAccountId.Trim();
        var folder = options.ImapFolder.Trim();

        var items = await mailboxClient
            .FetchUnreadAsync(cancellationToken)
            .ConfigureAwait(false);

        var mapped = new List<IncomingEmail>(items.Count);
        foreach (var item in items)
        {
            mapped.Add(MapMessage(item, accountId, folder));
        }

        logger.LogInformation(
            "IMAP receiver fetched unread count={Count} mode=Imap",
            mapped.Count);

        return mapped;
    }

    public async Task MarkAsProcessedAsync(
        EmailReceiptHandle receiptHandle,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(receiptHandle);

        var options = emailOptions.Value;
        var coordinates = ImapReceiptHandleCodec.Decode(
            receiptHandle,
            options.ImapAccountId,
            options.ImapFolder);

        await mailboxClient
            .MarkSeenAsync(coordinates.UidValidity, coordinates.Uid, cancellationToken)
            .ConfigureAwait(false);

        logger.LogInformation(
            "IMAP receiver marked message processed receiptKind={ReceiptKind}",
            receiptHandle.Kind);
    }

    private IncomingEmail MapMessage(
        ImapMailboxItem item,
        string accountId,
        string folder)
    {
        var message = item.Message;
        var receiptValue = ImapReceiptHandleCodec.Encode(
            new ImapReceiptCoordinates(
                accountId,
                folder,
                item.UidValidity,
                item.Uid));

        var hasTextBody = !string.IsNullOrWhiteSpace(message.TextBody);
        var body = hasTextBody
            ? message.TextBody
            : htmlConverter.Convert(message.HtmlBody);

        var mailbox = message.From.Mailboxes.FirstOrDefault();
        var messageId = string.IsNullOrWhiteSpace(message.MessageId)
            ? null
            : message.MessageId;

        var receivedAt = message.Date == default
            ? DateTime.UtcNow
            : message.Date.UtcDateTime;

        return new IncomingEmail(
            MessageId: messageId,
            ReceiptHandle: new EmailReceiptHandle(EmailReceiptKind.Imap, receiptValue),
            FromAddress: mailbox?.Address,
            FromDisplayName: string.IsNullOrWhiteSpace(mailbox?.Name) ? null : mailbox!.Name,
            Subject: message.Subject,
            Body: body,
            IsHtml: false,
            ReceivedAt: receivedAt,
            Attachments: MapAttachments(message));
    }

    private static IReadOnlyList<IncomingEmailAttachment> MapAttachments(MimeMessage message)
    {
        var attachments = new List<IncomingEmailAttachment>();

        foreach (var attachment in message.Attachments)
        {
            if (attachment is not MimePart part)
            {
                continue;
            }

            var fileName = part.FileName;
            if (string.IsNullOrWhiteSpace(fileName))
            {
                fileName = part.ContentType?.Name;
            }

            if (string.IsNullOrWhiteSpace(fileName))
            {
                fileName = "attachment";
            }

            var contentType = part.ContentType?.MimeType;
            if (string.IsNullOrWhiteSpace(contentType))
            {
                contentType = "application/octet-stream";
            }

            var fileSize = part.ContentDisposition?.Size ?? 0L;
            if (fileSize <= 0 && part.Content is not null)
            {
                try
                {
                    using var stream = part.Content.Open();
                    if (stream.CanSeek)
                    {
                        fileSize = stream.Length;
                    }
                }
                catch
                {
                    fileSize = 0;
                }
            }

            attachments.Add(new IncomingEmailAttachment(
                FileName: fileName,
                ContentType: contentType,
                FileSize: fileSize < 0 ? 0 : fileSize));
        }

        return attachments;
    }
}
