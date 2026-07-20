namespace VSHelpDesk.Application.Features.MailProcessing.ProcessIncomingEmails;

public sealed record ProcessIncomingEmailFailure(
    string Code,
    string ItemReference);

public sealed record ProcessIncomingEmailsResult(
    string ReceiverMode,
    int FetchedCount,
    int CreatedTickets,
    int CustomerReplies,
    int ReopenedTickets,
    int AlreadyProcessed,
    int AcknowledgementsSent,
    int AcknowledgementsFailed,
    int Quarantined,
    int RetryableFailures,
    IReadOnlyList<string> CreatedTicketNumbers,
    IReadOnlyList<ProcessIncomingEmailFailure> Failures);
