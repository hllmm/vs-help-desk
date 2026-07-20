namespace VSHelpDesk.Application.Features.MailProcessing.ProcessIncomingEmails;

/// <summary>
/// Day 8: fetch unread via configured receiver, log safely, optional SMTP probe.
/// Ticket creation / ack per message is Day 9.
/// </summary>
public sealed record ProcessIncomingEmailsCommand;
