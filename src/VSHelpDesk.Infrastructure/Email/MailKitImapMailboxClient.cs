using MailKit;
using MailKit.Net.Imap;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MimeKit;
using VSHelpDesk.Application.Abstractions.Email;

namespace VSHelpDesk.Infrastructure.Email;

/// <summary>
/// Scoped MailKit IMAP session: connect once, fetch unread, mark seen with UIDVALIDITY check.
/// Narrow gateway seam for test counting (Task 5); production path delegates via MailKitImapFolderGateway.
/// </summary>
public sealed class MailKitImapMailboxClient : IImapMailboxClient
{
    private readonly EmailOptions options;
    private readonly ILogger<MailKitImapMailboxClient> logger;
    private readonly IMailboxQuotaSettings quota;
    private readonly IImapFolderGateway? injectedGateway;
    private readonly ImapClient client = new();
    private IMailFolder? folder;
    private bool disposed;

    public MailKitImapMailboxClient(
        IOptions<EmailOptions> emailOptions,
        ILogger<MailKitImapMailboxClient> logger,
        IOptions<MailboxQuotaOptions>? quotaOptions = null,
        IMailboxQuotaSettings? quotaSettings = null)
    {
        this.options = emailOptions.Value;
        this.logger = logger;
        this.quota = quotaSettings ?? quotaOptions?.Value ?? new MailboxQuotaOptions();
        injectedGateway = null;
    }

    public MailKitImapMailboxClient(
        IOptions<EmailOptions> emailOptions,
        ILogger<MailKitImapMailboxClient> logger,
        IImapFolderGateway gateway,
        IOptions<MailboxQuotaOptions>? quotaOptions = null,
        IMailboxQuotaSettings? quotaSettings = null)
    {
        this.options = emailOptions.Value;
        this.logger = logger;
        this.quota = quotaSettings ?? quotaOptions?.Value ?? new MailboxQuotaOptions();
        injectedGateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
    }

    public MailKitImapMailboxClient(
        IImapFolderGateway gateway,
        IMailboxQuotaSettings quotaSettings)
        : this(Options.Create(new EmailOptions
        {
            ImapHost = "localhost",
            ImapPort = 993,
            ImapSecurityMode = MailTransportSecurityMode.None,
            ImapUsername = "test",
            ImapPassword = "test",
            ImapAccountId = "test-account",
            ImapFolder = "INBOX",
            SmtpHost = "localhost",
            SmtpPort = 25
        }), NullLogger<MailKitImapMailboxClient>.Instance, gateway, null, quotaSettings)
    {
    }

    public MailKitImapMailboxClient(
        IImapFolderGateway gateway,
        MailboxQuotaOptions quotaOptions)
        : this(gateway, (IMailboxQuotaSettings)quotaOptions)
    {
    }

    public async Task<IReadOnlyList<ImapMailboxItem>> FetchUnreadAsync(
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        var gateway = await GetGatewayAsync(cancellationToken).ConfigureAwait(false);

        var uids = await gateway.SearchUnseenAsync(cancellationToken).ConfigureAwait(false);

        var uidValidity = gateway.UidValidity;
        var take = Math.Min(uids.Count, quota.MaxMessagesPerRun);
        var limitedUids = take == uids.Count ? uids : uids.Take(take).ToList();

        // Pre-fetch sizes to skip oversized raw messages without downloading bodies.
        Dictionary<uint, uint?> sizeByUid = new();
        if (limitedUids.Count > 0)
        {
            try
            {
                sizeByUid = await gateway.FetchSizesAsync(limitedUids, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    ex,
                    "IMAP fetch sizes failed host={ImapHost} port={ImapPort} — falling back to per-message check",
                    options.ImapHost,
                    options.ImapPort);
                sizeByUid = new Dictionary<uint, uint?>();
            }
        }

        var items = new List<ImapMailboxItem>(take);

        foreach (var uid in limitedUids)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Blocker 3: for SIZE above limit, do not call GetMessageAsync and do not serialize into MemoryStream.
            // Return metadata-only oversized item (RawMessageTooLarge) for durable quarantine in Application layer. No fake MimeMessage.
            if (sizeByUid.TryGetValue(uid, out var size) && size.HasValue && size.Value > quota.MaxRawMessageBytes)
            {
                logger.LogWarning(
                    "IMAP message oversized uid={Uid} size={Size} maxRawMessageBytes={MaxRawMessageBytes} metadata-only, will be durably quarantined",
                    uid,
                    size.Value,
                    quota.MaxRawMessageBytes);
                items.Add(new ImapMailboxItem(uidValidity, uid, null, size.Value, ImapItemDisposition.RawMessageTooLarge));
                continue;
            }

            var message = await gateway.FetchMessageAsync(uid, cancellationToken).ConfigureAwait(false);

            // Fallback raw size check after download only for servers without SIZE; do not use MemoryStream for oversized already handled.
            // For normal messages, Estimate via WriteTo is acceptable but we already have Size for most; only do if Size missing.
            // Control order Raw -> Aggregate -> Ready (Task 6 will add aggregate/SizeUnavailable; here we keep Raw check before Ready).
            long? rawSize = null;
            if (!sizeByUid.ContainsKey(uid))
            {
                rawSize = EstimateRawSize(message);
                if (rawSize > quota.MaxRawMessageBytes)
                {
                    logger.LogWarning(
                        "IMAP message oversized after fetch uid={Uid} rawSize={RawSize} maxRawMessageBytes={MaxRawMessageBytes} will be quarantined after durable record",
                        uid,
                        rawSize,
                        quota.MaxRawMessageBytes);
                    items.Add(new ImapMailboxItem(uidValidity, uid, null, rawSize, ImapItemDisposition.RawMessageTooLarge));
                    continue;
                }
            }

            long? effectiveRawSize = rawSize ?? (sizeByUid.TryGetValue(uid, out var foundSize) && foundSize.HasValue ? (long?)foundSize.Value : null);
            items.Add(new ImapMailboxItem(uidValidity, uid, message, effectiveRawSize, ImapItemDisposition.Ready));
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

    private async Task<IImapFolderGateway> GetGatewayAsync(CancellationToken cancellationToken)
    {
        if (injectedGateway is not null)
        {
            return injectedGateway;
        }

        await EnsureOpenAsync(cancellationToken).ConfigureAwait(false);

        var openFolder = folder
            ?? throw new InvalidOperationException("IMAP folder is not open.");

        return new MailKitImapFolderGateway(openFolder);
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
