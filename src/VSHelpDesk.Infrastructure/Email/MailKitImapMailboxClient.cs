using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using VSHelpDesk.Application.Abstractions.Email;

namespace VSHelpDesk.Infrastructure.Email;

/// <summary>
/// Scoped MailKit IMAP session: connect once, fetch unread, mark seen with UIDVALIDITY check.
/// </summary>
public sealed class MailKitImapMailboxClient(
    IOptions<EmailOptions> emailOptions,
    ILogger<MailKitImapMailboxClient> logger,
    IOptions<MailboxQuotaOptions>? quotaOptions = null,
    IMailboxQuotaSettings? quotaSettings = null) : IImapMailboxClient
{
    private readonly EmailOptions options = emailOptions.Value;
    private readonly IMailboxQuotaSettings quota = quotaSettings ?? quotaOptions?.Value ?? new MailboxQuotaOptions();
    private readonly ImapClient client = new();
    private IMailFolder? folder;
    private bool disposed;

    public async Task<IReadOnlyList<ImapMailboxItem>> FetchUnreadAsync(
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        await EnsureOpenAsync(cancellationToken).ConfigureAwait(false);

        var openFolder = folder
            ?? throw new InvalidOperationException("IMAP folder is not open.");

        var uids = await openFolder
            .SearchAsync(SearchQuery.NotSeen, cancellationToken)
            .ConfigureAwait(false);

        var uidValidity = openFolder.UidValidity;
        var take = Math.Min(uids.Count, quota.MaxMessagesPerRun);
        var limitedUids = take == uids.Count ? uids : uids.Take(take).ToList();

        // Pre-fetch sizes to skip oversized raw messages without downloading bodies.
        Dictionary<uint, uint?> sizeByUid = new();
        if (limitedUids.Count > 0)
        {
            try
            {
                var summaries = await openFolder.FetchAsync(
                    limitedUids,
                    MessageSummaryItems.UniqueId | MessageSummaryItems.Size,
                    cancellationToken).ConfigureAwait(false);
                foreach (var s in summaries)
                {
                    if (s.UniqueId.IsValid)
                    {
                        sizeByUid[s.UniqueId.Id] = s.Size;
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    ex,
                    "IMAP fetch sizes failed host={ImapHost} port={ImapPort} — falling back to per-message check",
                    options.ImapHost,
                    options.ImapPort);
            }
        }

        var items = new List<ImapMailboxItem>(take);

        foreach (var uid in limitedUids)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Blocker 3: for SIZE above limit, do not call GetMessageAsync and do not serialize into MemoryStream.
            // Return metadata-only oversized item (IsOversized) for durable quarantine in Application layer.
            if (sizeByUid.TryGetValue(uid.Id, out var size) && size.HasValue && size.Value > quota.MaxRawMessageBytes)
            {
                logger.LogWarning(
                    "IMAP message oversized uid={Uid} size={Size} maxRawMessageBytes={MaxRawMessageBytes} metadata-only, will be durably quarantined",
                    uid.Id,
                    size.Value,
                    quota.MaxRawMessageBytes);
                var placeholder = new MimeMessage();
                placeholder.MessageId = $"<oversized-{uid.Id}@placeholder>";
                placeholder.Subject = "[Oversized message]";
                placeholder.From.Add(new MailboxAddress("unknown", "unknown@example.invalid"));
                placeholder.Body = new TextPart("plain") { Text = "[Oversized message content not downloaded]" };
                // Use Date.Now for received
                placeholder.Date = DateTimeOffset.UtcNow;
                items.Add(new ImapMailboxItem(uidValidity, uid.Id, placeholder, size.Value, IsOversized: true));
                continue;
            }

            var message = await openFolder
                .GetMessageAsync(uid, cancellationToken)
                .ConfigureAwait(false);

            // Fallback raw size check after download only for servers without SIZE; do not use MemoryStream for oversized already handled.
            // For normal messages, Estimate via WriteTo is acceptable but we already have Size for most; only do if Size missing.
            long? rawSize = null;
            if (!sizeByUid.ContainsKey(uid.Id))
            {
                rawSize = EstimateRawSize(message);
                if (rawSize > quota.MaxRawMessageBytes)
                {
                    logger.LogWarning(
                        "IMAP message oversized after fetch uid={Uid} rawSize={RawSize} maxRawMessageBytes={MaxRawMessageBytes} will be quarantined after durable record",
                        uid.Id,
                        rawSize,
                        quota.MaxRawMessageBytes);
                    items.Add(new ImapMailboxItem(uidValidity, uid.Id, message, rawSize, IsOversized: true));
                    continue;
                }
            }

            items.Add(new ImapMailboxItem(uidValidity, uid.Id, message, sizeByUid.GetValueOrDefault(uid.Id), IsOversized: false));
        }

        if (uids.Count > take)
        {
            logger.LogWarning(
                "IMAP fetch capped host={ImapHost} port={ImapPort} totalUnseen={Total} cappedTo={Capped} maxMessagesPerRun={Max} quarantined=quota-exceeded",
                options.ImapHost,
                options.ImapPort,
                uids.Count,
                items.Count,
                quota.MaxMessagesPerRun);
        }

        logger.LogInformation(
            "IMAP fetch unread completed host={ImapHost} port={ImapPort} count={Count}",
            options.ImapHost,
            options.ImapPort,
            items.Count);

        return items;
    }

    private static long EstimateRawSize(MimeMessage message)
    {
        try
        {
            using var ms = new MemoryStream();
            message.WriteTo(ms);
            return ms.Length;
        }
        catch
        {
            // Fallback approximation.
            var approx = (message.Headers.ToString()?.Length ?? 0)
                + (message.TextBody?.Length ?? 0)
                + (message.HtmlBody?.Length ?? 0);
            return approx;
        }
    }

    public async Task MarkSeenAsync(
        uint expectedUidValidity,
        uint uid,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        await EnsureOpenAsync(cancellationToken).ConfigureAwait(false);

        var openFolder = folder
            ?? throw new InvalidOperationException("IMAP folder is not open.");

        if (openFolder.UidValidity != expectedUidValidity)
        {
            throw new InvalidOperationException(
                "IMAP UIDVALIDITY changed since the receipt was issued; refusing to mark Seen.");
        }

        await openFolder
            .AddFlagsAsync(new UniqueId(uid), MessageFlags.Seen, silent: true, cancellationToken)
            .ConfigureAwait(false);

        logger.LogInformation(
            "IMAP mark seen succeeded host={ImapHost} port={ImapPort}",
            options.ImapHost,
            options.ImapPort);
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;

        try
        {
            if (client.IsConnected)
            {
                await client.DisconnectAsync(quit: true).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "IMAP disconnect failed host={ImapHost} port={ImapPort}",
                options.ImapHost,
                options.ImapPort);
        }
        finally
        {
            client.Dispose();
        }
    }

    private async Task EnsureOpenAsync(CancellationToken cancellationToken)
    {
        if (folder is { IsOpen: true })
        {
            return;
        }

        var secureSocket = MailTransportSecurity.ToSecureSocketOptions(options.ImapSecurityMode);

        if (!client.IsConnected)
        {
            await client
                .ConnectAsync(options.ImapHost, options.ImapPort, secureSocket, cancellationToken)
                .ConfigureAwait(false);
        }

        if (!client.IsAuthenticated)
        {
            await client
                .AuthenticateAsync(options.ImapUsername, options.ImapPassword, cancellationToken)
                .ConfigureAwait(false);
        }

        var folderName = options.ImapFolder.Trim();
        folder = string.Equals(folderName, "INBOX", StringComparison.OrdinalIgnoreCase)
            ? client.Inbox
            : await client.GetFolderAsync(folderName, cancellationToken).ConfigureAwait(false);

        if (!folder.IsOpen)
        {
            await folder.OpenAsync(FolderAccess.ReadWrite, cancellationToken).ConfigureAwait(false);
        }
    }
}
