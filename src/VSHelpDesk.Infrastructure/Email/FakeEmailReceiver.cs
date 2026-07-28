using System.Runtime.CompilerServices;
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

    public async IAsyncEnumerable<IncomingEmail> ReadUnreadAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var options = emailOptions.Value;
        var count = 0;
        foreach (var email in unread
                     .Where(message =>
                         !processedIds.Contains(
                             message.ReceiptHandle.Value))
                     .Take(options.MaxUnreadBatchSize))
        {
            cancellationToken.ThrowIfCancellationRequested();
            count++;
            yield return email;
            await Task.Yield();
        }

        logger.LogInformation(
            "Fake email receiver fetched unread count={Count} mode=Fake",
            count);
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
