using System.Runtime.CompilerServices;
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

    public async IAsyncEnumerable<ImapMailboxItem> FetchUnreadAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        var gateway = await GetGatewayAsync(cancellationToken).ConfigureAwait(false);

        var uids = await gateway.SearchUnseenAsync(cancellationToken).ConfigureAwait(false);

        var uidValidity = gateway.UidValidity;
        var take = Math.Min(uids.Count, quota.MaxMessagesPerRun);
        var limited = take == uids.Count ? uids : uids.Take(take).ToList();

        Dictionary<uint, uint?> sizes = new();
        if (limited.Count > 0)
        {
            try
            {
                sizes = await gateway.FetchSizesAsync(limited, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    ex,
                    "SIZE fetch failed host={ImapHost} port={ImapPort} — falling back to per-message bounded check",
                    options.ImapHost,
                    options.ImapPort);
                sizes = new Dictionary<uint, uint?>();
            }
        }

        long aggregate = 0;

        foreach (var uid in limited)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (sizes.TryGetValue(uid, out var s) && s.HasValue)
            {
                if (s.Value > quota.MaxRawMessageBytes)
                {
                    logger.LogWarning(
                        "IMAP message oversized uid={Uid} size={Size} maxRawMessageBytes={MaxRawMessageBytes} metadata-only, will be durably quarantined",
                        uid,
                        s.Value,
                        quota.MaxRawMessageBytes);
                    yield return new ImapMailboxItem(uidValidity, uid, null, s.Value, ImapItemDisposition.RawMessageTooLarge);
                    continue;
                }

                if (aggregate + s.Value > quota.MaxAggregateBytesPerRun)
                {
                    yield return new ImapMailboxItem(uidValidity, uid, null, s.Value, ImapItemDisposition.AggregateBudgetExceeded);
                    continue;
                }

                var msg = await gateway.FetchMessageAsync(uid, cancellationToken).ConfigureAwait(false);
                aggregate += s.Value;
                yield return new ImapMailboxItem(uidValidity, uid, msg, s.Value, ImapItemDisposition.Ready);
            }
            else
            {
                // SIZE null branch — bounded raw fetch
                long remaining = quota.MaxAggregateBytesPerRun - aggregate;
                long limit = Math.Min(quota.MaxRawMessageBytes, remaining) + 1;
                if (limit <= 0)
                {
                    yield return new ImapMailboxItem(uidValidity, uid, null, null, ImapItemDisposition.AggregateBudgetExceeded);
                    continue;
                }

                ImapMailboxItem? pending = null;
                try
                {
                    var (bytes, read) = await gateway.FetchRawBoundedAsync(uid, limit, cancellationToken).ConfigureAwait(false);
                    if (read > quota.MaxRawMessageBytes)
                    {
                        pending = new ImapMailboxItem(uidValidity, uid, null, read, ImapItemDisposition.RawMessageTooLarge);
                    }
                    else if (read > remaining)
                    {
                        pending = new ImapMailboxItem(uidValidity, uid, null, read, ImapItemDisposition.AggregateBudgetExceeded);
                    }
                    else
                    {
                        var msg = MimeMessage.Load(new MemoryStream(bytes));
                        aggregate += read;
                        pending = new ImapMailboxItem(uidValidity, uid, msg, read, ImapItemDisposition.Ready);
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (NotSupportedException)
                {
                    pending = new ImapMailboxItem(uidValidity, uid, null, null, ImapItemDisposition.SizeUnavailable);
                }
                catch
                {
                    pending = new ImapMailboxItem(uidValidity, uid, null, null, ImapItemDisposition.SizeUnavailable);
                }

                if (pending is not null)
                {
                    yield return pending;
                }
            }
        }

        if (uids.Count > take)
        {
            logger.LogWarning(
                "IMAP fetch capped host={ImapHost} port={ImapPort} totalUnseen={Total} cappedTo={Capped} maxMessagesPerRun={Max} quarantined=quota-exceeded",
                options.ImapHost,
                options.ImapPort,
                uids.Count,
                take,
                quota.MaxMessagesPerRun);
        }

        logger.LogInformation(
            "IMAP fetch unread completed host={ImapHost} port={ImapPort} count={Count}",
            options.ImapHost,
            options.ImapPort,
            take);
    }

    async Task<IReadOnlyList<ImapMailboxItem>> IImapMailboxClient.FetchUnreadAsync(CancellationToken cancellationToken)
    {
        var items = new List<ImapMailboxItem>();
        await foreach (var item in FetchUnreadAsync(cancellationToken).WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            items.Add(item);
        }

        return items;
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
