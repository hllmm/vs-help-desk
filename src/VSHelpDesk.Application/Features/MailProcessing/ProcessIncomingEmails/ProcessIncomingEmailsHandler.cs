using Microsoft.Extensions.Logging;
using VSHelpDesk.Application.Abstractions.Email;
using VSHelpDesk.Application.Abstractions.Persistence;
using VSHelpDesk.Application.Common.Models;
using VSHelpDesk.Application.Features.Tickets.CreateTicket;
using VSHelpDesk.Application.Features.Tickets.ReplyToTicket;
using VSHelpDesk.Domain.Tickets;

namespace VSHelpDesk.Application.Features.MailProcessing.ProcessIncomingEmails;

/// <summary>
/// UC-002 / UC-006 / UC-009: unread → new ticket | customer reply | reopen.
/// </summary>
public sealed class ProcessIncomingEmailsHandler(
    IEmailReceiver emailReceiver,
    IEmailSender emailSender,
    IEmailBoundarySettings emailBoundarySettings,
    IApplicationDbContext applicationDbContext,
    CreateTicketHandler createTicketHandler,
    AppendCustomerReplyHandler appendCustomerReplyHandler,
    ILogger<ProcessIncomingEmailsHandler> logger)
{
    public async Task<Result<ProcessIncomingEmailsResult>> HandleAsync(
        ProcessIncomingEmailsCommand command,
        CancellationToken cancellationToken)
    {
        _ = command;
        var mode = emailBoundarySettings.ReceiverMode;

        logger.LogInformation(
            "ProcessIncomingEmails started receiverMode={ReceiverMode}",
            mode);

        IReadOnlyList<IncomingEmail> unread;
        try
        {
            unread = await emailReceiver.FetchUnreadAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "ProcessIncomingEmails fetch failed receiverMode={ReceiverMode}",
                mode);
            return Result.Failure<ProcessIncomingEmailsResult>(
                "Failed to fetch unread emails from the configured receiver.");
        }

        var messageIds = new List<string>();
        var createdTicketNumbers = new List<string>();
        var createdTickets = 0;
        var customerReplies = 0;
        var reopenedTickets = 0;
        var alreadyProcessed = 0;
        var ackSent = 0;
        var ackFailed = 0;

        foreach (var mail in unread)
        {
            if (string.IsNullOrWhiteSpace(mail.MessageId))
            {
                logger.LogWarning("ProcessIncomingEmails skipped message without MessageId");
                continue;
            }

            var messageId = mail.MessageId.Trim();
            messageIds.Add(messageId);

            if (TicketNumberParser.TryFindInText(mail.Subject, out var subjectTicketNumber) &&
                applicationDbContext.Tickets.Any(ticket => ticket.TicketNumber == subjectTicketNumber))
            {
                var replyResult = await appendCustomerReplyHandler.HandleAsync(
                    new AppendCustomerReplyCommand(
                        MessageId: messageId,
                        TicketNumber: subjectTicketNumber,
                        Content: string.IsNullOrWhiteSpace(mail.Body) ? "(empty body)" : mail.Body,
                        IsHtml: mail.IsHtml),
                    cancellationToken);

                if (replyResult.IsFailure)
                {
                    logger.LogError(
                        "ProcessIncomingEmails reply failed messageId={MessageId} ticketNumber={TicketNumber} error={Error}",
                        messageId,
                        subjectTicketNumber,
                        replyResult.Error);
                    continue;
                }

                var reply = replyResult.Value!;
                if (reply.WasAlreadyProcessed)
                {
                    alreadyProcessed++;
                    logger.LogInformation(
                        "ProcessIncomingEmails reply already processed messageId={MessageId} ticketNumber={TicketNumber}",
                        messageId,
                        reply.TicketNumber);
                }
                else
                {
                    customerReplies++;
                    if (reply.WasReopened)
                    {
                        reopenedTickets++;
                    }

                    logger.LogInformation(
                        "ProcessIncomingEmails customer reply messageId={MessageId} ticketNumber={TicketNumber} statusBefore={StatusBefore} statusAfter={StatusAfter} reopened={Reopened}",
                        messageId,
                        reply.TicketNumber,
                        reply.StatusBefore,
                        reply.StatusAfter,
                        reply.WasReopened);
                }

                await SafeMarkProcessedAsync(messageId, cancellationToken);
                continue;
            }

            var createResult = await createTicketHandler.HandleAsync(
                new CreateTicketCommand(
                    MessageId: messageId,
                    Subject: string.IsNullOrWhiteSpace(mail.Subject) ? "(no subject)" : mail.Subject,
                    CustomerName: string.IsNullOrWhiteSpace(mail.FromDisplayName)
                        ? mail.FromAddress
                        : mail.FromDisplayName,
                    CustomerEmail: mail.FromAddress,
                    Content: mail.Body,
                    IsHtml: mail.IsHtml),
                cancellationToken);

            if (createResult.IsFailure)
            {
                logger.LogError(
                    "ProcessIncomingEmails create failed messageId={MessageId} error={Error}",
                    messageId,
                    createResult.Error);
                continue;
            }

            var created = createResult.Value!;
            if (created.WasAlreadyProcessed)
            {
                alreadyProcessed++;
                logger.LogInformation(
                    "ProcessIncomingEmails already processed messageId={MessageId} ticketNumber={TicketNumber}",
                    messageId,
                    created.TicketNumber);
                await SafeMarkProcessedAsync(messageId, cancellationToken);
                continue;
            }

            createdTickets++;
            createdTicketNumbers.Add(created.TicketNumber);
            logger.LogInformation(
                "ProcessIncomingEmails created ticket messageId={MessageId} ticketNumber={TicketNumber} ticketId={TicketId}",
                messageId,
                created.TicketNumber,
                created.TicketId);

            // Commit done in CreateTicketHandler. Ack is best-effort after commit (BR-002).
            // SMTP is not exactly-once: operational retry may send another ack if create path re-runs without processed record.
            try
            {
                await emailSender.SendAsync(
                    BuildAcknowledgement(mail, created.TicketNumber),
                    cancellationToken);
                ackSent++;
                logger.LogInformation(
                    "ProcessIncomingEmails ack sent messageId={MessageId} ticketNumber={TicketNumber} to={ToAddress}",
                    messageId,
                    created.TicketNumber,
                    mail.FromAddress);
            }
            catch (Exception ex)
            {
                ackFailed++;
                logger.LogError(
                    ex,
                    "ProcessIncomingEmails ack failed after commit messageId={MessageId} ticketNumber={TicketNumber} to={ToAddress}",
                    messageId,
                    created.TicketNumber,
                    mail.FromAddress);
            }

            await SafeMarkProcessedAsync(messageId, cancellationToken);
        }

        logger.LogInformation(
            "ProcessIncomingEmails finished receiverMode={ReceiverMode} fetched={Fetched} created={Created} replies={Replies} reopened={Reopened} alreadyProcessed={AlreadyProcessed} ackSent={AckSent} ackFailed={AckFailed}",
            mode,
            unread.Count,
            createdTickets,
            customerReplies,
            reopenedTickets,
            alreadyProcessed,
            ackSent,
            ackFailed);

        return Result.Success(new ProcessIncomingEmailsResult(
            mode,
            unread.Count,
            createdTickets,
            customerReplies,
            reopenedTickets,
            alreadyProcessed,
            ackSent,
            ackFailed,
            messageIds,
            createdTicketNumbers));
    }

    private async Task SafeMarkProcessedAsync(string messageId, CancellationToken cancellationToken)
    {
        try
        {
            await emailReceiver.MarkAsProcessedAsync(messageId, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "ProcessIncomingEmails MarkAsProcessed failed messageId={MessageId}",
                messageId);
        }
    }

    private static EmailMessage BuildAcknowledgement(IncomingEmail mail, string ticketNumber) =>
        new(
            ToAddress: mail.FromAddress,
            ToDisplayName: string.IsNullOrWhiteSpace(mail.FromDisplayName)
                ? mail.FromAddress
                : mail.FromDisplayName,
            Subject: $"[{ticketNumber}] We received your support request",
            Body:
            $"Hello,{Environment.NewLine}{Environment.NewLine}" +
            $"We received your message and opened ticket {ticketNumber}.{Environment.NewLine}" +
            $"Please keep {ticketNumber} in the subject when you reply.{Environment.NewLine}{Environment.NewLine}" +
            "VS Help Desk",
            IsHtml: false);
}
