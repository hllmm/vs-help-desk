using Microsoft.Extensions.Logging;
using VSHelpDesk.Application.Abstractions.Email;
using VSHelpDesk.Application.Abstractions.Persistence;
using VSHelpDesk.Application.Common.Models;
using VSHelpDesk.Application.Features.MailProcessing.Acknowledgements;
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
    IEmailBoundarySettings emailBoundarySettings,
    IApplicationDbContext applicationDbContext,
    CreateTicketHandler createTicketHandler,
    AppendCustomerReplyHandler appendCustomerReplyHandler,
    AcknowledgementDispatcher acknowledgementDispatcher,
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
            // Task 3 boundary: stable identity from Message-ID or receipt hash.
            // Full typed normalization enters the EF path in Task 7.
            var identity = InboundEmailIdentityFactory.Create(mail);
            var idempotencyKey = identity.IdempotencyKey;
            messageIds.Add(idempotencyKey);

            if (string.IsNullOrWhiteSpace(mail.FromAddress))
            {
                skippedInvalid++;
                logger.LogWarning(
                    "ProcessIncomingEmails skipped poison message without From idempotencyKey={IdempotencyKey}",
                    idempotencyKey);
                await QuarantineAsync(identity, mail.ReceiptHandle, cancellationToken);
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
                        "ProcessIncomingEmails subject matched ticket but From mismatch idempotencyKey={IdempotencyKey} ticketNumber={TicketNumber} from={FromAddress} expected={CustomerEmail}; creating new ticket",
                        idempotencyKey,
                        subjectTicketNumber,
                        mail.FromAddress,
                        ticket.CustomerEmail);
                    // Fall through to create a new ticket for this sender.
                }
                else
                {
                    var replyResult = await appendCustomerReplyHandler.HandleAsync(
                        new AppendCustomerReplyCommand(
                            IdempotencyKey: identity.IdempotencyKey,
                            SourceMessageId: identity.SourceMessageId,
                            TicketNumber: subjectTicketNumber,
                            Content: body,
                            FromAddress: mail.FromAddress),
                        cancellationToken);

                    if (replyResult.IsFailure)
                    {
                        logger.LogError(
                            "ProcessIncomingEmails reply failed idempotencyKey={IdempotencyKey} ticketNumber={TicketNumber} error={Error}",
                            idempotencyKey,
                            subjectTicketNumber,
                            replyResult.Error);
                        // Permanent validation → quarantine so the mail is not retried forever.
                        if (IsPermanentReplyFailure(replyResult.Error))
                        {
                            skippedInvalid++;
                            await QuarantineAsync(identity, mail.ReceiptHandle, cancellationToken);
                        }

                        continue;
                    }

                    var reply = replyResult.Value!;
                    if (reply.WasAlreadyProcessed)
                    {
                        alreadyProcessed++;
                        logger.LogInformation(
                            "ProcessIncomingEmails reply already processed idempotencyKey={IdempotencyKey} ticketNumber={TicketNumber}",
                            idempotencyKey,
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
                            "ProcessIncomingEmails customer reply idempotencyKey={IdempotencyKey} ticketNumber={TicketNumber} statusBefore={StatusBefore} statusAfter={StatusAfter} reopened={Reopened}",
                            idempotencyKey,
                            reply.TicketNumber,
                            reply.StatusBefore,
                            reply.StatusAfter,
                            reply.WasReopened);
                    }

                    await SafeMarkProcessedAsync(mail.ReceiptHandle, cancellationToken);
                    continue;
                }
            }

            var createResult = await createTicketHandler.HandleAsync(
                new CreateTicketCommand(
                    IdempotencyKey: identity.IdempotencyKey,
                    SourceMessageId: identity.SourceMessageId,
                    Subject: string.IsNullOrWhiteSpace(mail.Subject) ? "(no subject)" : mail.Subject,
                    CustomerName: string.IsNullOrWhiteSpace(mail.FromDisplayName)
                        ? mail.FromAddress
                        : mail.FromDisplayName,
                    CustomerEmail: mail.FromAddress,
                    Content: body),
                cancellationToken);

            if (createResult.IsFailure)
            {
                logger.LogError(
                    "ProcessIncomingEmails create failed idempotencyKey={IdempotencyKey} error={Error}",
                    idempotencyKey,
                    createResult.Error);
                skippedInvalid++;
                await QuarantineAsync(identity, mail.ReceiptHandle, cancellationToken);
                continue;
            }

            var created = createResult.Value!;
            if (created.WasAlreadyProcessed)
            {
                alreadyProcessed++;
                logger.LogInformation(
                    "ProcessIncomingEmails already processed idempotencyKey={IdempotencyKey} ticketNumber={TicketNumber}",
                    idempotencyKey,
                    created.TicketNumber);
                await SafeMarkProcessedAsync(mail.ReceiptHandle, cancellationToken);
                continue;
            }

            createdTickets++;
            createdTicketNumbers.Add(created.TicketNumber);
            logger.LogInformation(
                "ProcessIncomingEmails created ticket idempotencyKey={IdempotencyKey} ticketNumber={TicketNumber} ticketId={TicketId}",
                idempotencyKey,
                created.TicketNumber,
                created.TicketId);

            // Single acknowledgement path: durable AttemptAsync after create commit.
            var ack = await acknowledgementDispatcher.AttemptAsync(
                created.ProcessedEmailMessageId,
                cancellationToken);
            if (ack.Attempted)
            {
                if (ack.Sent)
                {
                    ackSent++;
                    logger.LogInformation(
                        "ProcessIncomingEmails ack sent ticketNumber={TicketNumber} processedEmailMessageId={ProcessedEmailMessageId}",
                        created.TicketNumber,
                        created.ProcessedEmailMessageId);
                }
                else
                {
                    ackFailed++;
                }
            }

            await SafeMarkProcessedAsync(mail.ReceiptHandle, cancellationToken);
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
    private async Task QuarantineAsync(
        InboundEmailIdentity identity,
        EmailReceiptHandle receiptHandle,
        CancellationToken cancellationToken)
    {
        try
        {
            var already = applicationDbContext.ProcessedEmailMessages
                .Any(row => row.IdempotencyKey == identity.IdempotencyKey);
            if (!already)
            {
                applicationDbContext.Add(ProcessedEmailMessage.ForQuarantine(
                    identity.IdempotencyKey,
                    sourceMessageId: identity.SourceMessageId,
                    processedAtUtc: timeProvider.GetUtcNow().UtcDateTime));
                await applicationDbContext.SaveChangesAsync(cancellationToken);
            }
        }
        catch (Exception ex)
        {
            applicationDbContext.ClearTrackedChanges();
            logger.LogWarning(
                ex,
                "ProcessIncomingEmails quarantine save failed idempotencyKey={IdempotencyKey}",
                identity.IdempotencyKey);
        }

        await SafeMarkProcessedAsync(receiptHandle, cancellationToken);
    }

    private static bool IsPermanentReplyFailure(string? error) =>
        !string.IsNullOrWhiteSpace(error) &&
        (error.Contains("not found", StringComparison.OrdinalIgnoreCase) ||
         error.Contains("does not match", StringComparison.OrdinalIgnoreCase) ||
         error.Contains("required", StringComparison.OrdinalIgnoreCase));

    private async Task SafeMarkProcessedAsync(
        EmailReceiptHandle receiptHandle,
        CancellationToken cancellationToken)
    {
        try
        {
            await emailReceiver.MarkAsProcessedAsync(receiptHandle, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "ProcessIncomingEmails MarkAsProcessed failed receiptKind={ReceiptKind}",
                receiptHandle.Kind);
        }
    }
}
