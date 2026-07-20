using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using VSHelpDesk.Application.Abstractions.Email;
using VSHelpDesk.Application.Common.Exceptions;
using VSHelpDesk.Application.Common.Models;

namespace VSHelpDesk.Application.Features.MailProcessing.ProcessIncomingEmails;

/// <summary>
/// UC-002 / UC-006 / UC-009 job orchestrator: lease → retry acks → fetch → per-receipt scope.
/// Does not own DbContext, create/reply handlers, or the email sender.
/// </summary>
public sealed class ProcessIncomingEmailsHandler(
    IEmailReceiver emailReceiver,
    IEmailBoundarySettings emailBoundarySettings,
    IInboundEmailItemProcessorFactory itemProcessorFactory,
    IProcessIncomingEmailsGate processIncomingEmailsGate,
    ILogger<ProcessIncomingEmailsHandler> logger)
{
    public async Task<Result<ProcessIncomingEmailsResult>> HandleAsync(
        ProcessIncomingEmailsCommand command,
        CancellationToken cancellationToken)
    {
        _ = command;

        await using var lease =
            await processIncomingEmailsGate.TryAcquireAsync(cancellationToken)
            ?? throw new JobAlreadyRunningException();

        return await HandleCoreAsync(cancellationToken);
    }

    private async Task<Result<ProcessIncomingEmailsResult>> HandleCoreAsync(
        CancellationToken cancellationToken)
    {
        var mode = emailBoundarySettings.ReceiverMode;

        logger.LogInformation(
            "ProcessIncomingEmails started receiverMode={ReceiverMode}",
            mode);

        var ackSent = 0;
        var ackFailed = 0;

        try
        {
            var retrySummary = await itemProcessorFactory.RetryDueAcknowledgementsAsync(
                cancellationToken);
            ackSent += retrySummary.Sent;
            ackFailed += retrySummary.Failed;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "ProcessIncomingEmails acknowledgement retry pass failed");
            // Continue to fetch/process receipts; durable pending rows remain for a later run.
        }

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

        var createdTicketNumbers = new List<string>();
        var failures = new List<ProcessIncomingEmailFailure>();
        var createdTickets = 0;
        var customerReplies = 0;
        var reopenedTickets = 0;
        var alreadyProcessed = 0;
        var quarantined = 0;
        var retryableFailures = 0;

        foreach (var mail in unread)
        {
            var itemReference = ToItemReference(mail.ReceiptHandle);

            InboundEmailItemResult item;
            try
            {
                item = await itemProcessorFactory.ProcessAsync(mail, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(
                    ex,
                    "ProcessIncomingEmails item failed itemReference={ItemReference}",
                    itemReference);
                retryableFailures++;
                failures.Add(new ProcessIncomingEmailFailure("processing-failed", itemReference));
                continue;
            }

            var shouldMark = false;
            switch (item.Outcome)
            {
                case InboundEmailItemOutcome.CreatedTicket:
                    createdTickets++;
                    if (!string.IsNullOrWhiteSpace(item.TicketNumber))
                    {
                        createdTicketNumbers.Add(item.TicketNumber);
                    }

                    if (item.AcknowledgementSent)
                    {
                        ackSent++;
                    }

                    if (item.AcknowledgementFailed)
                    {
                        ackFailed++;
                    }

                    // Mark even when SMTP failed: durable retry state was committed.
                    shouldMark = true;
                    break;

                case InboundEmailItemOutcome.AppendedReply:
                    customerReplies++;
                    if (item.WasReopened)
                    {
                        reopenedTickets++;
                    }

                    shouldMark = true;
                    break;

                case InboundEmailItemOutcome.AlreadyProcessed:
                    alreadyProcessed++;
                    shouldMark = true;
                    break;

                case InboundEmailItemOutcome.Quarantined:
                    quarantined++;
                    shouldMark = true;
                    break;

                case InboundEmailItemOutcome.RetryableFailure:
                    retryableFailures++;
                    failures.Add(new ProcessIncomingEmailFailure(
                        item.FailureCode ?? "retryable-failure",
                        itemReference));
                    shouldMark = false;
                    break;
            }

            if (!shouldMark)
            {
                continue;
            }

            try
            {
                await emailReceiver.MarkAsProcessedAsync(mail.ReceiptHandle, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(
                    ex,
                    "ProcessIncomingEmails MarkAsProcessed failed itemReference={ItemReference} receiptKind={ReceiptKind}",
                    itemReference,
                    mail.ReceiptHandle.Kind);
                failures.Add(new ProcessIncomingEmailFailure("mark-seen-failed", itemReference));
            }
        }

        logger.LogInformation(
            "ProcessIncomingEmails finished receiverMode={ReceiverMode} fetched={Fetched} created={Created} replies={Replies} reopened={Reopened} alreadyProcessed={AlreadyProcessed} ackSent={AckSent} ackFailed={AckFailed} quarantined={Quarantined} retryableFailures={RetryableFailures} failures={FailureCount}",
            mode,
            unread.Count,
            createdTickets,
            customerReplies,
            reopenedTickets,
            alreadyProcessed,
            ackSent,
            ackFailed,
            quarantined,
            retryableFailures,
            failures.Count);

        return Result.Success(new ProcessIncomingEmailsResult(
            mode,
            unread.Count,
            createdTickets,
            customerReplies,
            reopenedTickets,
            alreadyProcessed,
            ackSent,
            ackFailed,
            quarantined,
            retryableFailures,
            createdTicketNumbers,
            failures));
    }

    /// <summary>
    /// Correlatable receipt fingerprint for public job results — never the raw handle.
    /// </summary>
    internal static string ToItemReference(EmailReceiptHandle receiptHandle)
    {
        ArgumentNullException.ThrowIfNull(receiptHandle);

        return Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(receiptHandle.Value)))
            .ToLowerInvariant();
    }
}
