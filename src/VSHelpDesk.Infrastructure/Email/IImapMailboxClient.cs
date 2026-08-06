using System.Runtime.CompilerServices;
using VSHelpDesk.Application.Abstractions.Email;

namespace VSHelpDesk.Infrastructure.Email;

/// <summary>
/// Narrow IMAP session seam: fetch unread items and mark seen by UID + UIDVALIDITY.
/// </summary>
public interface IImapMailboxClient : IAsyncDisposable
{
    IAsyncEnumerable<ImapMailboxItem> FetchUnreadAsync(
        CancellationToken cancellationToken = default);

    Task MarkSeenAsync(
        uint expectedUidValidity,
        uint uid,
        CancellationToken cancellationToken);
}
