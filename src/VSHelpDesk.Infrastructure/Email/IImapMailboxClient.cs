using MailKit;
using MimeKit;

namespace VSHelpDesk.Infrastructure.Email;

public sealed record ImapMailboxItem(
    uint UidValidity,
    uint Uid,
    Envelope? Envelope = null,
    MimeMessage? Message = null,
    long? DeclaredSize = null,
    string? BoundaryViolation = null);

/// <summary>
/// Narrow IMAP session seam: fetch unread items and mark seen by UID + UIDVALIDITY.
/// </summary>
public interface IImapMailboxClient : IAsyncDisposable
{
    IAsyncEnumerable<ImapMailboxItem> ReadUnreadAsync(
        int maxCount,
        long maxMessageSizeBytes,
        CancellationToken cancellationToken);

    Task MarkSeenAsync(
        uint expectedUidValidity,
        uint uid,
        CancellationToken cancellationToken);
}
