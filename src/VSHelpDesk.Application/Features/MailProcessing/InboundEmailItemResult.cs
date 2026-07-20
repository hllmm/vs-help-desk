namespace VSHelpDesk.Application.Features.MailProcessing;

public enum InboundEmailItemOutcome
{
    CreatedTicket = 1,
    AppendedReply = 2,
    AlreadyProcessed = 3,
    Quarantined = 4,
    RetryableFailure = 5
}

public sealed record InboundEmailItemResult(
    InboundEmailItemOutcome Outcome,
    string? IdempotencyKey,
    string? TicketNumber,
    bool WasReopened,
    bool AcknowledgementSent,
    bool AcknowledgementFailed,
    string? FailureCode);
