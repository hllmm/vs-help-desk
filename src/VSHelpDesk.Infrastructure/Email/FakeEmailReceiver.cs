using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VSHelpDesk.Application.Abstractions.Email;

namespace VSHelpDesk.Infrastructure.Email;

/// <summary>
/// Deterministic unread inbox for Development/tests when real IMAP is unavailable.
/// </summary>
public sealed class FakeEmailReceiver(
    IOptions<EmailOptions> emailOptions,
    ILogger<FakeEmailReceiver> logger) : IEmailReceiver
{
    private readonly List<IncomingEmail> unread = [];

    private readonly HashSet<string> processedIds = new(StringComparer.Ordinal);

    /// <summary>Explicit fixture injection for isolated automated tests.</summary>
    public FakeEmailReceiver(
        IOptions<EmailOptions> emailOptions,
        ILogger<FakeEmailReceiver> logger,
        IEnumerable<IncomingEmail> initialMessages)
        : this(emailOptions, logger)
    {
        ArgumentNullException.ThrowIfNull(initialMessages);
        unread.AddRange(initialMessages);
    }

    public Task<IReadOnlyList<IncomingEmail>> FetchUnreadAsync(CancellationToken cancellationToken = default)
    {
        _ = emailOptions.Value;
        var batch = unread
            .Where(message => !processedIds.Contains(message.ReceiptHandle.Value))
            .ToList();

        logger.LogInformation(
            "Fake email receiver fetched unread count={Count} mode=Fake",
            batch.Count);

        return Task.FromResult<IReadOnlyList<IncomingEmail>>(batch);
    }

    public Task MarkAsProcessedAsync(
        EmailReceiptHandle receiptHandle,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(receiptHandle);

        if (!string.IsNullOrWhiteSpace(receiptHandle.Value))
        {
            processedIds.Add(receiptHandle.Value);
        }

        logger.LogInformation(
            "Fake email receiver marked message processed receiptKind={ReceiptKind}",
            receiptHandle.Kind);

        return Task.CompletedTask;
    }
}
