using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VSHelpDesk.Application.Abstractions.Email;

namespace VSHelpDesk.Infrastructure.Email;

/// <summary>
/// Placeholder when ReceiverMode=Imap until a real MailKit IMAP adapter is wired (later day).
/// Fails clearly so Fake and Imap modes cannot be confused.
/// </summary>
public sealed class NotConfiguredImapEmailReceiver(
    IOptions<EmailOptions> emailOptions,
    ILogger<NotConfiguredImapEmailReceiver> logger) : IEmailReceiver
{
    public Task<IReadOnlyList<IncomingEmail>> FetchUnreadAsync(CancellationToken cancellationToken = default)
    {
        var options = emailOptions.Value;
        logger.LogError(
            "IMAP receiver selected but not implemented host={ImapHost} port={ImapPort}",
            options.ImapHost,
            options.ImapPort);

        throw new InvalidOperationException(
            "Email:ReceiverMode is Imap, but the IMAP adapter is not implemented yet. " +
            "Use Email:ReceiverMode=Fake for Development/tests, or implement MailKit IMAP later.");
    }

    public Task MarkAsProcessedAsync(string messageId, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
