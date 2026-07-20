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
    private readonly List<IncomingEmail> unread =
    [
        new(
            MessageId: "<fake-unread-001@vshelpdesk.local>",
            FromAddress: "customer.one@example.test",
            FromDisplayName: "Customer One",
            Subject: "Printer offline in office A",
            Body: "Hello, our office printer stopped working this morning.",
            IsHtml: false,
            ReceivedAt: DateTime.UtcNow.AddMinutes(-15),
            Attachments: Array.Empty<IncomingEmailAttachment>()),
        new(
            MessageId: "<fake-unread-002@vshelpdesk.local>",
            FromAddress: "customer.two@example.test",
            FromDisplayName: "Customer Two",
            Subject: "VPN access request",
            Body: "Please enable VPN for the new contractor.",
            IsHtml: false,
            ReceivedAt: DateTime.UtcNow.AddMinutes(-5),
            Attachments: Array.Empty<IncomingEmailAttachment>())
    ];

    private readonly HashSet<string> processedIds = new(StringComparer.Ordinal);

    public Task<IReadOnlyList<IncomingEmail>> FetchUnreadAsync(CancellationToken cancellationToken = default)
    {
        _ = emailOptions.Value;
        var batch = unread
            .Where(message => !processedIds.Contains(message.MessageId))
            .ToList();

        logger.LogInformation(
            "Fake email receiver fetched unread count={Count} mode=Fake",
            batch.Count);

        return Task.FromResult<IReadOnlyList<IncomingEmail>>(batch);
    }

    public Task MarkAsProcessedAsync(string messageId, CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(messageId))
        {
            processedIds.Add(messageId.Trim());
        }

        logger.LogInformation(
            "Fake email receiver marked message processed messageId={MessageId}",
            messageId);

        return Task.CompletedTask;
    }
}
