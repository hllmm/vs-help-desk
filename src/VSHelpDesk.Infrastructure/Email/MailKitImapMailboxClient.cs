using System.Runtime.CompilerServices;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;

namespace VSHelpDesk.Infrastructure.Email;

/// <summary>
/// Scoped MailKit IMAP session: connect once, fetch unread, mark seen with UIDVALIDITY check.
/// </summary>
public sealed class MailKitImapMailboxClient(
    IOptions<EmailOptions> emailOptions,
    ILogger<MailKitImapMailboxClient> logger) : IImapMailboxClient
{
    private readonly EmailOptions options = emailOptions.Value;
    private readonly ImapClient client = new();
    private IMailFolder? folder;
    private bool disposed;

    public async IAsyncEnumerable<ImapMailboxItem> ReadUnreadAsync(
        int maxCount,
        long maxMessageSizeBytes,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        await EnsureOpenAsync(cancellationToken).ConfigureAwait(false);

        var openFolder = folder
            ?? throw new InvalidOperationException("IMAP folder is not open.");

        var uids = await openFolder
            .SearchAsync(SearchQuery.NotSeen, cancellationToken)
            .ConfigureAwait(false);

        var selected = uids.Take(maxCount).ToList();
        var summaries = await openFolder.FetchAsync(
                selected,
                MessageSummaryItems.UniqueId
                    | MessageSummaryItems.Size
                    | MessageSummaryItems.Envelope,
                cancellationToken)
            .ConfigureAwait(false);
        var yieldedCount = 0;

        foreach (var summary in summaries.OrderBy(item => item.UniqueId.Id))
        {
            cancellationToken.ThrowIfCancellationRequested();
            long? declaredSize = summary.Size is uint size
                ? (long)size
                : null;
            if (declaredSize > maxMessageSizeBytes)
            {
                yieldedCount++;
                yield return new ImapMailboxItem(
                    openFolder.UidValidity,
                    summary.UniqueId.Id,
                    summary.Envelope,
                    Message: null,
                    declaredSize,
                    BoundaryViolation: "message-size-exceeded");
                continue;
            }

            var message = await openFolder
                .GetMessageAsync(summary.UniqueId, cancellationToken)
                .ConfigureAwait(false);
            yieldedCount++;
            yield return new ImapMailboxItem(
                openFolder.UidValidity,
                summary.UniqueId.Id,
                summary.Envelope,
                message,
                declaredSize,
                BoundaryViolation: null);
        }

        logger.LogInformation(
            "IMAP fetch unread completed host={ImapHost} port={ImapPort} count={Count}",
            options.ImapHost,
            options.ImapPort,
            yieldedCount);
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
