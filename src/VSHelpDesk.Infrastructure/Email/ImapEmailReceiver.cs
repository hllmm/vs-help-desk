using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using VSHelpDesk.Application.Abstractions.Email;
using VSHelpDesk.Application.Features.MailProcessing;
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
    ILogger<ImapEmailReceiver> logger,
    IOptions<MailboxQuotaOptions>? quotaOptions = null) : IEmailReceiver
{
    private const int CopyBufferSize = 8192;
    private readonly MailboxQuotaOptions quota = quotaOptions?.Value ?? new MailboxQuotaOptions();

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
        string body;
        if (hasTextBody)
        {
            body = message.TextBody!;
        }
        else
        {
            var htmlBody = message.HtmlBody ?? string.Empty;
            // Bound HTML parsing: pre-truncate huge bodies to avoid unbounded HtmlAgilityPack work.
            var htmlLimit = InboundMailLimits.MaxBodyLength * 4;
            if (htmlBody.Length > htmlLimit)
            {
                logger.LogWarning(
                    "IMAP html body truncated before conversion originalLength={OriginalLength} limit={Limit} quarantined=quota-exceeded",
                    htmlBody.Length,
                    htmlLimit);
                htmlBody = htmlBody[..htmlLimit];
            }

            body = htmlConverter.Convert(htmlBody);
        }

        var mailbox = message.From.Mailboxes.FirstOrDefault();
        // MimeKit's MessageId property returns the token without angle brackets.
        // Identity requires <left@right>; canonicalize at this boundary only.
        var messageId = CanonicalizeMimeKitMessageId(message.MessageId);

        var receivedAt = message.Date == default
            ? DateTime.UtcNow
            : message.Date.UtcDateTime;

        // Trust-boundary: Authentication-Results is added by the trusted MTA and must
        // be stripped from client-supplied headers upstream. We pass the raw header
        // through as IncomingEmail.AuthenticationResults; verification requires
        // dmarc=pass (case-insensitive). Missing or failing header is untrusted.
        var authenticationResults = message.Headers["Authentication-Results"];

        return new IncomingEmail(
            MessageId: messageId,
            ReceiptHandle: new EmailReceiptHandle(EmailReceiptKind.Imap, receiptValue),
            FromAddress: mailbox?.Address,
            FromDisplayName: string.IsNullOrWhiteSpace(mailbox?.Name) ? null : mailbox!.Name,
            Subject: message.Subject,
            Body: body,
            IsHtml: false,
            ReceivedAt: receivedAt,
            Attachments: MapAttachments(message),
            AuthenticationResults: authenticationResults);
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
        var maxAttachmentsPerMessage = quota.MaxAttachmentsPerMessage > 0
            ? quota.MaxAttachmentsPerMessage
            : InboundMailLimits.MaxAttachmentsPerMessage;
        // Do not truncate: collect all attachments so InboundEmailNormalizer can quarantine >MaxAttachmentsPerMessage (I2).
        // Capacity grows as needed; excessive counts are bounded downstream by normalizer assurance.
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
                if (stream.CanSeek && stream.Length > maxFileSizeBytes)
                {
                    logger.LogWarning(
                        "IMAP attachment omitted as oversized fileName={FileName} streamLength={StreamLength} maxFileSizeBytes={MaxFileSizeBytes}",
                        fileName,
                        stream.Length,
                        maxFileSizeBytes);
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
                    if (total > maxFileSizeBytes)
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
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    ex,
                    "IMAP attachment content load failed; omitting fileName={FileName}",
                    fileName);
            }
        }

        if (attachments.Count > maxAttachmentsPerMessage)
        {
            logger.LogWarning(
                "IMAP attachments exceed per-message limit count={Count} maxAttachmentsPerMessage={Max} quarantined=quota-exceeded",
                attachments.Count,
                maxAttachmentsPerMessage);
        }

        return attachments;
    }
}
