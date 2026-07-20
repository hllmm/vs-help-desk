namespace VSHelpDesk.Application.Features.MailProcessing.ProcessIncomingEmails;

public sealed record ProcessIncomingEmailsResult(
    string ReceiverMode,
    int FetchedCount,
    IReadOnlyList<string> MessageIds,
    bool SmtpProbeSent);
