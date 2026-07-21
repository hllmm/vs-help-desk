using MimeKit;

namespace VSHelpDesk.Infrastructure.Email;

public sealed record ImapMailboxItem(
    uint UidValidity,
    uint Uid,
    MimeMessage Message);

/// <summary>
/// Narrow IMAP session seam: fetch unread items and mark seen by UID + UIDVALIDITY.
/// </summary>
public interface IImapMailboxClient : IAsyncDisposable
{
    Task<IReadOnlyList<ImapMailboxItem>> FetchUnreadAsync(
        CancellationToken cancellationToken);

    Task MarkSeenAsync(
        uint expectedUidValidity,
        uint uid,
        CancellationToken cancellationToken);
}
