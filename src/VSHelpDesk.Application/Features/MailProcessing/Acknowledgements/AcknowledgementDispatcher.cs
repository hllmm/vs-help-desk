using Microsoft.Extensions.Logging;
using VSHelpDesk.Application.Abstractions.Email;
using VSHelpDesk.Application.Abstractions.Persistence.Repositories;
using VSHelpDesk.Application.Common.Localization;
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
    IProcessedEmailRepository processedEmailRepository,
    ITicketRepository ticketRepository,
    IUnitOfWork unitOfWork,
    IEmailSender sender,
    TimeProvider timeProvider,
    ILogger<AcknowledgementDispatcher> logger,
    IEmailTemplateService? templateService = null,
    IMessageProvider? messages = null)
{
    private const string SafeSmtpFailureMessage = "SMTP acknowledgement failed.";

    public async Task<AcknowledgementAttemptResult> AttemptAsync(
        Guid processedEmailMessageId,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var processed = await processedEmailRepository.GetByIdAsync(processedEmailMessageId, cancellationToken);

        if (processed is null || !processed.IsAcknowledgementDue(now))
        {
            return new AcknowledgementAttemptResult(Attempted: false, Sent: false);
        }

        if (processed.TicketId is null)
        {
            return new AcknowledgementAttemptResult(Attempted: false, Sent: false);
        }

        var ticket = await ticketRepository.GetByIdAsync(processed.TicketId.Value, cancellationToken: cancellationToken);
        if (ticket is null)
        {
            return new AcknowledgementAttemptResult(Attempted: false, Sent: false);
        }

        // Catch only around SMTP send. Database/cancellation failures must propagate
        // and must not be recorded as acknowledgement delivery failures.
        try
        {
            await sender.SendAsync(BuildAcknowledgement(ticket), cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(
                ex,
                "Acknowledgement delivery failed processedEmailMessageId={ProcessedEmailMessageId} ticketId={TicketId}",
                processed.Id,
                ticket.Id);
            processed.RecordAcknowledgementFailure(now, SafeSmtpFailureMessage);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return new AcknowledgementAttemptResult(Attempted: true, Sent: false);
        }

        processed.RecordAcknowledgementSent(now);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new AcknowledgementAttemptResult(Attempted: true, Sent: true);
    }

    public async Task<AcknowledgementDispatchSummary> RetryDueAsync(
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;

        // Database-translatable predicate only — never call IsAcknowledgementDue in the query.
        var dueIds = processedEmailRepository.GetListQueryable()
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

    private EmailMessage BuildAcknowledgement(Ticket ticket)
    {
        var subject = messages?.Get(MessageKeys.Email.AcknowledgementSubject, ticket.TicketNumber)
            ?? $"[{ticket.TicketNumber}] Destek talebinizi aldık";
        var rawBody = messages?.Get(
                MessageKeys.Email.AcknowledgementBody,
                ticket.TicketNumber,
                Environment.NewLine)
            ?? $"Merhaba,{Environment.NewLine}{Environment.NewLine}" +
               $"Mesajınızı aldık ve {ticket.TicketNumber} numaralı Ticket kaydını oluşturduk.{Environment.NewLine}" +
               $"Yanıt verirken lütfen konu satırında {ticket.TicketNumber} numarasını koruyun.{Environment.NewLine}{Environment.NewLine}" +
               "VS Help Desk";

        var body = templateService != null
            ? templateService.WrapInCorporateTemplate(subject, rawBody)
            : rawBody;
        var isHtml = templateService != null;

        return new EmailMessage(
            ToAddress: ticket.CustomerEmail,
            ToDisplayName: string.IsNullOrWhiteSpace(ticket.CustomerName)
                ? ticket.CustomerEmail
                : ticket.CustomerName,
            Subject: subject,
            Body: body,
            IsHtml: isHtml,
            TextBody: rawBody);
    }
}
