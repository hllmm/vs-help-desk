using Microsoft.Extensions.Logging;
using VSHelpDesk.Application.Abstractions.Email;
using VSHelpDesk.Application.Abstractions.Persistence;
using VSHelpDesk.Application.Common.Models;
using VSHelpDesk.Application.Features.Tickets.CreateTicket;
using VSHelpDesk.Application.Features.Tickets.ReplyToTicket;
using VSHelpDesk.Domain.Entities;
using VSHelpDesk.Domain.Tickets;

namespace VSHelpDesk.Application.Features.MailProcessing.ProcessIncomingEmails;

/// <summary>
/// UC-002 / UC-006 / UC-009: unread → new ticket | customer reply | reopen.
/// Single-flight gate prevents overlapping job runs in-process.
/// </summary>
public sealed class ProcessIncomingEmailsHandler(
    IEmailReceiver emailReceiver,
    IEmailSender emailSender,
    IEmailBoundarySettings emailBoundarySettings,
    IApplicationDbContext applicationDbContext,
    CreateTicketHandler createTicketHandler,
    AppendCustomerReplyHandler appendCustomerReplyHandler,
    TimeProvider timeProvider,
    IProcessIncomingEmailsGate processIncomingEmailsGate,
    ILogger<ProcessIncomingEmailsHandler> logger)
{
    public async Task<Result<ProcessIncomingEmailsResult>> HandleAsync(
        ProcessIncomingEmailsCommand command,
        CancellationToken cancellationToken)
    {
        _ = command;

        if (!await processIncomingEmailsGate.TryEnterAsync(cancellationToken))
        {
            logger.LogWarning("ProcessIncomingEmails skipped because another run is in progress");
            return Result.Failure<ProcessIncomingEmailsResult>(
                "Process-incoming-emails is already running.");
        }

        try
        {
            return await HandleCoreAsync(cancellationToken);
        }
        finally
        {
            processIncomingEmailsGate.Exit();
        }
    }

    private async Task<Result<ProcessIncomingEmailsResult>> HandleCoreAsync(
        CancellationToken cancellationToken)
    {
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
        var skippedInvalid = 0;

        foreach (var mail in unread)
        {
            if (string.IsNullOrWhiteSpace(mail.MessageId))
            {
                skippedInvalid++;
                logger.LogWarning(
                    "ProcessIncomingEmails skipped poison message without MessageId from={FromAddress}",
                    mail.FromAddress);
                continue;
            }

            var messageId = mail.MessageId.Trim();
            messageIds.Add(messageId);

            if (string.IsNullOrWhiteSpace(mail.FromAddress))
            {
                skippedInvalid++;
                logger.LogWarning(
                    "ProcessIncomingEmails skipped poison message without From messageId={MessageId}",
                    messageId);
                await QuarantineAsync(messageId, cancellationToken);
                continue;
            }

            var body = InboundMailLimits.NormalizeBody(mail.Body);

            if (TicketNumberParser.TryFindInText(mail.Subject, out var subjectTicketNumber) &&
                applicationDbContext.Tickets.Any(ticket => ticket.TicketNumber == subjectTicketNumber))
            {
                var ticket = applicationDbContext.Tickets
                    .First(candidate => candidate.TicketNumber == subjectTicketNumber);

                // From must match ticket customer (sender binding for real IMAP).
                if (!string.Equals(
                        ticket.CustomerEmail.Trim(),
                        mail.FromAddress.Trim(),
                        StringComparison.OrdinalIgnoreCase))
                {
                    logger.LogWarning(
                        "ProcessIncomingEmails subject matched ticket but From mismatch messageId={MessageId} ticketNumber={TicketNumber} from={FromAddress} expected={CustomerEmail}; creating new ticket",
                        messageId,
                        subjectTicketNumber,
                        mail.FromAddress,
                        ticket.CustomerEmail);
                    // Fall through to create a new ticket for this sender.
                }
                else
                {
                    var replyResult = await appendCustomerReplyHandler.HandleAsync(
                        new AppendCustomerReplyCommand(
                            MessageId: messageId,
                            TicketNumber: subjectTicketNumber,
                            Content: body,
                            IsHtml: false,
                            FromAddress: mail.FromAddress),
                        cancellationToken);

                    if (replyResult.IsFailure)
                    {
                        logger.LogError(
                            "ProcessIncomingEmails reply failed messageId={MessageId} ticketNumber={TicketNumber} error={Error}",
                            messageId,
                            subjectTicketNumber,
                            replyResult.Error);
                        // Permanent validation → quarantine so the mail is not retried forever.
                        if (IsPermanentReplyFailure(replyResult.Error))
                        {
                            skippedInvalid++;
                            await QuarantineAsync(messageId, cancellationToken);
                        }

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
            }

            var createResult = await createTicketHandler.HandleAsync(
                new CreateTicketCommand(
                    MessageId: messageId,
                    Subject: string.IsNullOrWhiteSpace(mail.Subject) ? "(no subject)" : mail.Subject,
                    CustomerName: string.IsNullOrWhiteSpace(mail.FromDisplayName)
                        ? mail.FromAddress
                        : mail.FromDisplayName,
                    CustomerEmail: mail.FromAddress,
                    Content: body,
                    IsHtml: false),
                cancellationToken);

            if (createResult.IsFailure)
            {
                logger.LogError(
                    "ProcessIncomingEmails create failed messageId={MessageId} error={Error}",
                    messageId,
                    createResult.Error);
                skippedInvalid++;
                await QuarantineAsync(messageId, cancellationToken);
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
            var processed = applicationDbContext.ProcessedEmailMessages
                .First(row => row.Id == created.ProcessedEmailMessageId);
            var attemptedAt = timeProvider.GetUtcNow().UtcDateTime;

            try
            {
                await emailSender.SendAsync(
                    BuildAcknowledgement(mail, created.TicketNumber),
                    cancellationToken);
                processed.RecordAcknowledgementSent(attemptedAt);
                ackSent++;
                logger.LogInformation(
                    "ProcessIncomingEmails ack sent ticketNumber={TicketNumber} processedEmailMessageId={ProcessedEmailMessageId}",
                    created.TicketNumber,
                    processed.Id);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(
                    ex,
                    "Acknowledgement delivery failed processedEmailMessageId={ProcessedEmailMessageId} ticketId={TicketId}",
                    processed.Id,
                    created.TicketId);
                processed.RecordAcknowledgementFailure(
                    attemptedAt,
                    "SMTP acknowledgement failed.");
                ackFailed++;
            }

            await applicationDbContext.SaveChangesAsync(cancellationToken);
            await SafeMarkProcessedAsync(messageId, cancellationToken);
        }

        logger.LogInformation(
            "ProcessIncomingEmails finished receiverMode={ReceiverMode} fetched={Fetched} created={Created} replies={Replies} reopened={Reopened} alreadyProcessed={AlreadyProcessed} ackSent={AckSent} ackFailed={AckFailed} skippedInvalid={SkippedInvalid}",
            mode,
            unread.Count,
            createdTickets,
            customerReplies,
            reopenedTickets,
            alreadyProcessed,
            ackSent,
            ackFailed,
            skippedInvalid);

        return Result.Success(new ProcessIncomingEmailsResult(
            mode,
            unread.Count,
            createdTickets,
            customerReplies,
            reopenedTickets,
            alreadyProcessed,
            ackSent,
            ackFailed,
            skippedInvalid,
            messageIds,
            createdTicketNumbers));
    }

    /// <summary>
    /// Permanent poison: record idempotency key without a ticket so re-fetch does not loop forever.
    /// </summary>
    private async Task QuarantineAsync(string messageId, CancellationToken cancellationToken)
    {
        try
        {
            var already = applicationDbContext.ProcessedEmailMessages
                .Any(row => row.IdempotencyKey == messageId);
            if (!already)
            {
                applicationDbContext.Add(ProcessedEmailMessage.ForQuarantine(
                    messageId,
                    sourceMessageId: messageId,
                    processedAtUtc: timeProvider.GetUtcNow().UtcDateTime));
                await applicationDbContext.SaveChangesAsync(cancellationToken);
            }
        }
        catch (Exception ex)
        {
            applicationDbContext.ClearTrackedChanges();
            logger.LogWarning(
                ex,
                "ProcessIncomingEmails quarantine save failed messageId={MessageId}",
                messageId);
        }

        await SafeMarkProcessedAsync(messageId, cancellationToken);
    }

    private static bool IsPermanentReplyFailure(string? error) =>
        !string.IsNullOrWhiteSpace(error) &&
        (error.Contains("not found", StringComparison.OrdinalIgnoreCase) ||
         error.Contains("does not match", StringComparison.OrdinalIgnoreCase) ||
         error.Contains("required", StringComparison.OrdinalIgnoreCase));

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
