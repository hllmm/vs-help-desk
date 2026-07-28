using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using VSHelpDesk.Application.Abstractions.Email;
using VSHelpDesk.Infrastructure.Storage;

namespace VSHelpDesk.Infrastructure.Email;

/// <summary>
/// Operational IMAP <see cref="IEmailReceiver"/>: maps MIME to boundary DTO and marks Seen by receipt.
/// </summary>
public sealed class ImapEmailReceiver(
    IOptions<EmailOptions> emailOptions,
    IOptions<FileStorageOptions> fileStorageOptions,
    IImapMailboxClient mailboxClient,
    HtmlToPlainTextConverter htmlConverter,
    ILogger<ImapEmailReceiver> logger) : IEmailReceiver
{
    private const int CopyBufferSize = 8192;

    public async IAsyncEnumerable<IncomingEmail> ReadUnreadAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var options = emailOptions.Value;
        var accountId = options.ImapAccountId.Trim();
        var folder = options.ImapFolder.Trim();

        var count = 0;
        await foreach (var item in mailboxClient
                           .ReadUnreadAsync(
                               options.MaxUnreadBatchSize,
                               options.MaxMessageSizeBytes,
                               cancellationToken)
                           .WithCancellation(cancellationToken)
                           .ConfigureAwait(false))
        {
            count++;
            if (!string.IsNullOrWhiteSpace(item.BoundaryViolation))
            {
                yield return MapBoundaryViolation(
                    item,
                    accountId,
                    folder);
                continue;
            }

            yield return MapMessage(item, accountId, folder);
        }

        logger.LogInformation(
            "IMAP receiver fetched unread count={Count} mode=Imap",
            count);
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
        var message = item.Message
            ?? throw new InvalidOperationException(
                "IMAP mailbox item did not contain message content.");
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
        // MimeKit's MessageId property returns the token without angle brackets.
        // Identity requires <left@right>; canonicalize at this boundary only.
        var messageId = CanonicalizeMimeKitMessageId(message.MessageId);

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

    private static IncomingEmail MapBoundaryViolation(
        ImapMailboxItem item,
        string accountId,
        string folder)
    {
        var envelopeMailbox =
            item.Envelope?.From?.Mailboxes.FirstOrDefault();
        return new IncomingEmail(
            MessageId: CanonicalizeMimeKitMessageId(
                item.Envelope?.MessageId),
            ReceiptHandle: new EmailReceiptHandle(
                EmailReceiptKind.Imap,
                ImapReceiptHandleCodec.Encode(
                    new ImapReceiptCoordinates(
                        accountId,
                        folder,
                        item.UidValidity,
                        item.Uid))),
            FromAddress: envelopeMailbox?.Address,
            FromDisplayName: envelopeMailbox?.Name,
            Subject: item.Envelope?.Subject,
            Body: null,
            IsHtml: false,
            ReceivedAt:
                item.Envelope?.Date?.UtcDateTime ?? DateTime.UtcNow,
            Attachments: [],
            BoundaryViolation: item.BoundaryViolation);
    }

    /// <summary>
    /// Maps MimeKit's unbracketed Message-Id into the RFC msg-id form expected by
    /// <c>InboundEmailIdentityFactory</c>. Does not invent IDs for invalid strings.
    /// </summary>
    public static string? CanonicalizeMimeKitMessageId(string? messageId)
    {
        if (string.IsNullOrWhiteSpace(messageId))
        {
            return null;
        }

        var trimmed = messageId.Trim();

        // Already angle-bracketed — pass through; identity validates shape/length.
        if (trimmed.Length >= 2 && trimmed[0] == '<' && trimmed[^1] == '>')
        {
            return trimmed;
        }

        // Bare id-left@id-right (printable ASCII, single @) → wrap.
        if (IsBareMessageIdToken(trimmed))
        {
            return $"<{trimmed}>";
        }

        // Clearly invalid: do not invent a Message-Id.
        return trimmed;
    }

    /// <summary>
    /// Single printable-ASCII token with exactly one '@' and non-empty left/right sides.
    /// </summary>
    private static bool IsBareMessageIdToken(string value)
    {
        // Minimum: a@b
        if (value.Length < 3)
        {
            return false;
        }

        var atIndex = -1;
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];

            // ASCII printable only; reject controls, space, and non-ASCII.
            if (c is < (char)0x21 or > (char)0x7E)
            {
                return false;
            }

            if (c is '<' or '>')
            {
                return false;
            }

            if (c == '@')
            {
                if (atIndex >= 0)
                {
                    return false;
                }

                atIndex = i;
            }
        }

        return atIndex > 0 && atIndex < value.Length - 1;
    }

    private IReadOnlyList<IncomingEmailAttachment> MapAttachments(MimeMessage message)
    {
        var maxFileSizeBytes = fileStorageOptions.Value.MaxFileSizeBytes;
        var options = emailOptions.Value;
        var attachments = new List<IncomingEmailAttachment>();
        long acceptedBytes = 0;

        foreach (var attachment in message.Attachments)
        {
            if (attachment is not MimePart part)
            {
                continue;
            }

            if (attachments.Count >= options.MaxAttachmentsPerMessage)
            {
                logger.LogWarning(
                    "IMAP attachment omitted because message attachment limit was reached maxAttachments={MaxAttachments}",
                    options.MaxAttachmentsPerMessage);
                break;
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

            var declaredSize = part.ContentDisposition?.Size ?? 0L;
            if (declaredSize > maxFileSizeBytes)
            {
                logger.LogWarning(
                    "IMAP attachment omitted as oversized fileName={FileName} declaredSize={DeclaredSize} maxFileSizeBytes={MaxFileSizeBytes}",
                    fileName,
                    declaredSize,
                    maxFileSizeBytes);
                continue;
            }

            var remainingAggregate =
                options.MaxTotalAttachmentBytesPerMessage
                - acceptedBytes;
            if (declaredSize > remainingAggregate)
            {
                logger.LogWarning(
                    "IMAP attachment omitted because aggregate byte limit would be exceeded fileName={FileName}",
                    fileName);
                continue;
            }

            if (part.Content is null)
            {
                attachments.Add(new IncomingEmailAttachment(
                    FileName: fileName,
                    ContentType: contentType,
                    FileSize: 0,
                    Content: Array.Empty<byte>()));
                continue;
            }

            try
            {
                using var stream = part.Content.Open();
                if (stream.CanSeek
                    && (stream.Length > maxFileSizeBytes
                        || stream.Length > remainingAggregate))
                {
                    logger.LogWarning(
                        "IMAP attachment omitted as oversized fileName={FileName} streamLength={StreamLength} maxFileSizeBytes={MaxFileSizeBytes} remainingAggregateBytes={RemainingAggregateBytes}",
                        fileName,
                        stream.Length,
                        maxFileSizeBytes,
                        remainingAggregate);
                    continue;
                }

                using var buffer = new MemoryStream();
                var chunk = new byte[CopyBufferSize];
                long total = 0;
                int read;
                var oversize = false;

                while ((read = stream.Read(chunk, 0, chunk.Length)) > 0)
                {
                    total += read;
                    // Hard stop at MaxFileSizeBytes + 1 so we never retain oversize bodies.
                    if (total > maxFileSizeBytes
                        || total > remainingAggregate)
                    {
                        oversize = true;
                        break;
                    }

                    buffer.Write(chunk, 0, read);
                }

                if (oversize)
                {
                    logger.LogWarning(
                        "IMAP attachment omitted as oversized during copy fileName={FileName} maxFileSizeBytes={MaxFileSizeBytes}",
                        fileName,
                        maxFileSizeBytes);
                    continue;
                }

                var content = buffer.ToArray();
                attachments.Add(new IncomingEmailAttachment(
                    FileName: fileName,
                    ContentType: contentType,
                    FileSize: content.Length,
                    Content: content));
                acceptedBytes += content.LongLength;
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    ex,
                    "IMAP attachment content load failed; omitting fileName={FileName}",
                    fileName);
            }
        }

        return attachments;
    }
}
