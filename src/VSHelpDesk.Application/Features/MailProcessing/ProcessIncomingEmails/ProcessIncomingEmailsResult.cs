namespace VSHelpDesk.Application.Features.MailProcessing.ProcessIncomingEmails;

public sealed record ProcessIncomingEmailsResult(
    string ReceiverMode,
    int FetchedCount,
    int CreatedTickets,
    int CustomerReplies,
    int ReopenedTickets,
    int AlreadyProcessed,
    int AckSent,
    int AckFailed,
    int SkippedInvalid,
    IReadOnlyList<string> MessageIds,
    IReadOnlyList<string> CreatedTicketNumbers);
