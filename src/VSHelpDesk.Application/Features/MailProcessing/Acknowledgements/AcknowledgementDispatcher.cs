using Microsoft.Extensions.Logging;
using VSHelpDesk.Application.Abstractions.Email;
using VSHelpDesk.Application.Abstractions.Persistence;
using VSHelpDesk.Domain.Entities;
using VSHelpDesk.Domain.Enums;

namespace VSHelpDesk.Application.Features.MailProcessing.Acknowledgements;

public sealed record AcknowledgementAttemptResult(
    bool Attempted,
    bool Sent);

public sealed record AcknowledgementDispatchSummary(
    int Attempted,
    int Sent,
    int Failed);

/// <summary>
/// Delivers new-ticket acknowledgements with durable Pending/Failed/Sent state (BR-002).
/// </summary>
public sealed class AcknowledgementDispatcher(
    IApplicationDbContext db,
    IEmailSender sender,
    TimeProvider timeProvider,
    ILogger<AcknowledgementDispatcher> logger)
{
    private const string SafeSmtpFailureMessage = "SMTP acknowledgement failed.";

    public async Task<AcknowledgementAttemptResult> AttemptAsync(
        Guid processedEmailMessageId,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var processed = db.ProcessedEmailMessages
            .FirstOrDefault(row => row.Id == processedEmailMessageId);

        if (processed is null || !processed.IsAcknowledgementDue(now))
        {
            return new AcknowledgementAttemptResult(Attempted: false, Sent: false);
        }

        if (processed.TicketId is null)
        {
            return new AcknowledgementAttemptResult(Attempted: false, Sent: false);
        }

        var ticket = db.Tickets.FirstOrDefault(candidate => candidate.Id == processed.TicketId);
        if (ticket is null)
        {
            return new AcknowledgementAttemptResult(Attempted: false, Sent: false);
        }

        try
        {
            await sender.SendAsync(BuildAcknowledgement(ticket), cancellationToken);
            processed.RecordAcknowledgementSent(now);
            await db.SaveChangesAsync(cancellationToken);
            return new AcknowledgementAttemptResult(Attempted: true, Sent: true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(
                ex,
                "Acknowledgement delivery failed processedEmailMessageId={ProcessedEmailMessageId} ticketId={TicketId}",
                processed.Id,
                ticket.Id);
            processed.RecordAcknowledgementFailure(now, SafeSmtpFailureMessage);
            await db.SaveChangesAsync(cancellationToken);
            return new AcknowledgementAttemptResult(Attempted: true, Sent: false);
        }
    }

    public async Task<AcknowledgementDispatchSummary> RetryDueAsync(
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;

        // Database-translatable predicate only — never call IsAcknowledgementDue in the query.
        var dueIds = db.ProcessedEmailMessages
            .Where(row =>
                (row.AcknowledgementStatus == AcknowledgementStatus.Pending
                 || row.AcknowledgementStatus == AcknowledgementStatus.Failed)
                && row.AcknowledgementNextAttemptAt != null
                && row.AcknowledgementNextAttemptAt <= now)
            .Select(row => row.Id)
            .ToList();

        var attempted = 0;
        var sent = 0;
        var failed = 0;

        foreach (var id in dueIds)
        {
            var result = await AttemptAsync(id, cancellationToken);
            if (!result.Attempted)
            {
                continue;
            }

            attempted++;
            if (result.Sent)
            {
                sent++;
            }
            else
            {
                failed++;
            }
        }

        return new AcknowledgementDispatchSummary(attempted, sent, failed);
    }

    private static EmailMessage BuildAcknowledgement(Ticket ticket) =>
        new(
            ToAddress: ticket.CustomerEmail,
            ToDisplayName: string.IsNullOrWhiteSpace(ticket.CustomerName)
                ? ticket.CustomerEmail
                : ticket.CustomerName,
            Subject: $"[{ticket.TicketNumber}] We received your support request",
            Body:
            $"Hello,{Environment.NewLine}{Environment.NewLine}" +
            $"We received your message and opened ticket {ticket.TicketNumber}.{Environment.NewLine}" +
            $"Please keep {ticket.TicketNumber} in the subject when you reply.{Environment.NewLine}{Environment.NewLine}" +
            "VS Help Desk",
            IsHtml: false);
}
