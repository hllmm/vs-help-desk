namespace VSHelpDesk.Application.Features.MailProcessing.ProcessIncomingEmails;

public sealed record ProcessIncomingEmailsResult(
    string ReceiverMode,
    int FetchedCount,
    int CreatedTickets,
    int AlreadyProcessed,
    int MatchedExistingSkipped,
    int AckSent,
    int AckFailed,
    IReadOnlyList<string> MessageIds,
    IReadOnlyList<string> CreatedTicketNumbers);
