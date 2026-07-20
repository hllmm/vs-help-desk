using VSHelpDesk.Domain.Enums;

namespace VSHelpDesk.Domain.Entities;

/// <summary>
/// Idempotency and lifecycle record for an inbound email (UC-002).
/// </summary>
public sealed class ProcessedEmailMessage
{
    public Guid Id { get; private set; } = Guid.NewGuid();

    public string IdempotencyKey { get; private set; } = string.Empty;

    public string? SourceMessageId { get; private set; }

    public ProcessedEmailDisposition Disposition { get; private set; }

    public string? ProcessingNote { get; private set; }

    public DateTime ProcessedAt { get; private set; }

    public Guid? TicketId { get; private set; }

    public AcknowledgementStatus AcknowledgementStatus { get; private set; }

    public int AcknowledgementAttempts { get; private set; }

    public DateTime? AcknowledgementLastAttemptAt { get; private set; }

    public DateTime? AcknowledgementNextAttemptAt { get; private set; }

    public DateTime? AcknowledgementSentAt { get; private set; }

    public string? AcknowledgementLastError { get; private set; }

    private ProcessedEmailMessage()
    {
    }

    public static ProcessedEmailMessage ForCreatedTicket(
        string idempotencyKey,
        string? sourceMessageId,
        DateTime processedAtUtc,
        Guid ticketId)
    {
        return new ProcessedEmailMessage
        {
            IdempotencyKey = idempotencyKey,
            SourceMessageId = sourceMessageId,
            Disposition = ProcessedEmailDisposition.CreatedTicket,
            ProcessedAt = processedAtUtc,
            TicketId = ticketId,
            AcknowledgementStatus = AcknowledgementStatus.Pending,
            AcknowledgementNextAttemptAt = processedAtUtc,
            AcknowledgementAttempts = 0
        };
    }

    public static ProcessedEmailMessage ForAppendedReply(
        string idempotencyKey,
        string? sourceMessageId,
        DateTime processedAtUtc,
        Guid ticketId)
    {
        return new ProcessedEmailMessage
        {
            IdempotencyKey = idempotencyKey,
            SourceMessageId = sourceMessageId,
            Disposition = ProcessedEmailDisposition.AppendedReply,
            ProcessedAt = processedAtUtc,
            TicketId = ticketId,
            AcknowledgementStatus = AcknowledgementStatus.NotRequired,
            AcknowledgementNextAttemptAt = null,
            AcknowledgementAttempts = 0
        };
    }

    public static ProcessedEmailMessage ForQuarantine(
        string idempotencyKey,
        string? sourceMessageId,
        DateTime processedAtUtc,
        string? processingNote = null)
    {
        return new ProcessedEmailMessage
        {
            IdempotencyKey = idempotencyKey,
            SourceMessageId = sourceMessageId,
            Disposition = ProcessedEmailDisposition.Quarantined,
            ProcessingNote = TrimTo(processingNote, 500),
            ProcessedAt = processedAtUtc,
            TicketId = null,
            AcknowledgementStatus = AcknowledgementStatus.NotRequired,
            AcknowledgementNextAttemptAt = null,
            AcknowledgementAttempts = 0
        };
    }

    /// <summary>Stop scheduling further acknowledgement SMTP after this many failures.</summary>
    public const int MaxAcknowledgementAttempts = 10;

    public void RecordAcknowledgementFailure(DateTime attemptedAtUtc, string safeError)
    {
        AcknowledgementAttempts++;
        AcknowledgementStatus = AcknowledgementStatus.Failed;
        AcknowledgementLastAttemptAt = attemptedAtUtc;
        AcknowledgementSentAt = null;
        AcknowledgementLastError = TrimTo(safeError, 500);
        // Cap infinite 60-minute retries for production operability.
        AcknowledgementNextAttemptAt = AcknowledgementAttempts >= MaxAcknowledgementAttempts
            ? null
            : attemptedAtUtc + GetRetryDelay(AcknowledgementAttempts);
    }

    public void RecordAcknowledgementSent(DateTime attemptedAtUtc)
    {
        AcknowledgementAttempts++;
        AcknowledgementStatus = AcknowledgementStatus.Sent;
        AcknowledgementLastAttemptAt = attemptedAtUtc;
        AcknowledgementSentAt = attemptedAtUtc;
        AcknowledgementNextAttemptAt = null;
        AcknowledgementLastError = null;
    }

    public bool IsAcknowledgementDue(DateTime nowUtc) =>
        AcknowledgementStatus is AcknowledgementStatus.Pending or AcknowledgementStatus.Failed
        && AcknowledgementNextAttemptAt is { } next
        && next <= nowUtc;

    private static TimeSpan GetRetryDelay(int failedAttemptNumber) =>
        failedAttemptNumber switch
        {
            <= 1 => TimeSpan.FromMinutes(1),
            2 => TimeSpan.FromMinutes(5),
            3 => TimeSpan.FromMinutes(15),
            _ => TimeSpan.FromMinutes(60)
        };

    private static string? TrimTo(string? value, int maxLength)
    {
        if (value is null)
        {
            return null;
        }

        return value.Length <= maxLength ? value : value[..maxLength];
    }
}
