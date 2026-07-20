using VSHelpDesk.Application.Abstractions.Email;
using VSHelpDesk.Application.Abstractions.Persistence;
using VSHelpDesk.Application.Common.Exceptions;
using VSHelpDesk.Application.Features.MailProcessing.Acknowledgements;
using VSHelpDesk.Application.Features.Tickets.CreateTicket;
using VSHelpDesk.Application.Features.Tickets.ReplyToTicket;
using VSHelpDesk.Domain.Entities;
using VSHelpDesk.Domain.Tickets;

namespace VSHelpDesk.Application.Features.MailProcessing.ProcessIncomingEmails;

/// <summary>
/// Processes a single inbound receipt inside one DI/DbContext scope.
/// Normalization runs before any entity is tracked.
/// </summary>
public sealed class InboundEmailItemProcessor(
    IApplicationDbContext applicationDbContext,
    CreateTicketHandler createTicketHandler,
    AppendCustomerReplyHandler appendCustomerReplyHandler,
    AcknowledgementDispatcher acknowledgementDispatcher,
    TimeProvider timeProvider,
    IDatabaseErrorClassifier databaseErrorClassifier) : IInboundEmailItemProcessor
{
    public async Task<InboundEmailItemResult> ProcessAsync(
        IncomingEmail email,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(email);

        var normalization = InboundEmailNormalizer.Normalize(email);
        var identity = normalization.Identity;

        if (normalization.Outcome == InboundEmailPolicyOutcome.Quarantine)
        {
            return await QuarantineAsync(
                identity,
                normalization.ProcessingNote,
                cancellationToken);
        }

        var normalized = normalization.Email
            ?? throw new InvalidOperationException(
                "Inbound normalizer returned Process without a normalized email.");

        var existing = FindProcessed(identity.IdempotencyKey);
        if (existing is not null)
        {
            return BuildAlreadyProcessed(identity.IdempotencyKey, existing);
        }

        try
        {
            if (TicketNumberParser.TryFindInText(normalized.Subject, out var ticketNumber) &&
                TryGetMatchingCustomerTicket(ticketNumber, normalized.FromAddress, out _))
            {
                return await AppendAsync(normalized, ticketNumber, cancellationToken);
            }

            return await CreateAsync(normalized, cancellationToken);
        }
        catch (OptimisticConcurrencyException)
        {
            return new InboundEmailItemResult(
                InboundEmailItemOutcome.RetryableFailure,
                identity.IdempotencyKey,
                TicketNumber: null,
                WasReopened: false,
                AcknowledgementSent: false,
                AcknowledgementFailed: false,
                FailureCode: "ticket-concurrency");
        }
    }

    private async Task<InboundEmailItemResult> QuarantineAsync(
        InboundEmailIdentity identity,
        string? processingNote,
        CancellationToken cancellationToken)
    {
        var existing = FindProcessed(identity.IdempotencyKey);
        if (existing is not null)
        {
            return BuildAlreadyProcessed(identity.IdempotencyKey, existing);
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        applicationDbContext.Add(ProcessedEmailMessage.ForQuarantine(
            identity.IdempotencyKey,
            sourceMessageId: identity.SourceMessageId,
            processedAtUtc: now,
            processingNote: processingNote));

        try
        {
            await applicationDbContext.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            applicationDbContext.ClearTrackedChanges();

            if (databaseErrorClassifier.IsProcessedEmailIdempotencyConflict(ex))
            {
                var afterRace = FindProcessed(identity.IdempotencyKey);
                if (afterRace is not null)
                {
                    return BuildAlreadyProcessed(identity.IdempotencyKey, afterRace);
                }

                return new InboundEmailItemResult(
                    InboundEmailItemOutcome.AlreadyProcessed,
                    identity.IdempotencyKey,
                    TicketNumber: null,
                    WasReopened: false,
                    AcknowledgementSent: false,
                    AcknowledgementFailed: false,
                    FailureCode: null);
            }

            throw;
        }

        return new InboundEmailItemResult(
            InboundEmailItemOutcome.Quarantined,
            identity.IdempotencyKey,
            TicketNumber: null,
            WasReopened: false,
            AcknowledgementSent: false,
            AcknowledgementFailed: false,
            FailureCode: null);
    }

    private async Task<InboundEmailItemResult> CreateAsync(
        NormalizedIncomingEmail normalized,
        CancellationToken cancellationToken)
    {
        var createResult = await createTicketHandler.HandleAsync(
            new CreateTicketCommand(
                IdempotencyKey: normalized.IdempotencyKey,
                SourceMessageId: normalized.SourceMessageId,
                Subject: normalized.Subject,
                CustomerName: normalized.FromDisplayName,
                CustomerEmail: normalized.FromAddress,
                Content: normalized.Body),
            cancellationToken);

        if (createResult.IsFailure)
        {
            // Normalized input should always validate; treat residual failure as unexpected.
            throw new InvalidOperationException(
                "CreateTicket failed after successful inbound normalization.");
        }

        var created = createResult.Value!;
        if (created.WasAlreadyProcessed)
        {
            return new InboundEmailItemResult(
                InboundEmailItemOutcome.AlreadyProcessed,
                normalized.IdempotencyKey,
                created.TicketNumber,
                WasReopened: false,
                AcknowledgementSent: false,
                AcknowledgementFailed: false,
                FailureCode: null);
        }

        var ack = await acknowledgementDispatcher.AttemptAsync(
            created.ProcessedEmailMessageId,
            cancellationToken);

        return new InboundEmailItemResult(
            InboundEmailItemOutcome.CreatedTicket,
            normalized.IdempotencyKey,
            created.TicketNumber,
            WasReopened: false,
            AcknowledgementSent: ack.Attempted && ack.Sent,
            AcknowledgementFailed: ack.Attempted && !ack.Sent,
            FailureCode: null);
    }

    private async Task<InboundEmailItemResult> AppendAsync(
        NormalizedIncomingEmail normalized,
        string ticketNumber,
        CancellationToken cancellationToken)
    {
        var replyResult = await appendCustomerReplyHandler.HandleAsync(
            new AppendCustomerReplyCommand(
                IdempotencyKey: normalized.IdempotencyKey,
                SourceMessageId: normalized.SourceMessageId,
                TicketNumber: ticketNumber,
                Content: normalized.Body,
                FromAddress: normalized.FromAddress),
            cancellationToken);

        if (replyResult.IsFailure)
        {
            // Pre-checked ticket/sender; residual validation failure is unexpected.
            throw new InvalidOperationException(
                "AppendCustomerReply failed after successful inbound matching.");
        }

        var reply = replyResult.Value!;
        if (reply.WasAlreadyProcessed)
        {
            return new InboundEmailItemResult(
                InboundEmailItemOutcome.AlreadyProcessed,
                normalized.IdempotencyKey,
                reply.TicketNumber,
                WasReopened: false,
                AcknowledgementSent: false,
                AcknowledgementFailed: false,
                FailureCode: null);
        }

        return new InboundEmailItemResult(
            InboundEmailItemOutcome.AppendedReply,
            normalized.IdempotencyKey,
            reply.TicketNumber,
            WasReopened: reply.WasReopened,
            AcknowledgementSent: false,
            AcknowledgementFailed: false,
            FailureCode: null);
    }

    private bool TryGetMatchingCustomerTicket(
        string ticketNumber,
        string fromAddress,
        out Ticket ticket)
    {
        var found = applicationDbContext.Tickets
            .FirstOrDefault(candidate => candidate.TicketNumber == ticketNumber);

        if (found is null ||
            !string.Equals(
                found.CustomerEmail.Trim(),
                fromAddress,
                StringComparison.OrdinalIgnoreCase))
        {
            ticket = null!;
            return false;
        }

        ticket = found;
        return true;
    }

    private ProcessedEmailMessage? FindProcessed(string idempotencyKey) =>
        applicationDbContext.ProcessedEmailMessages
            .FirstOrDefault(row => row.IdempotencyKey == idempotencyKey);

    private InboundEmailItemResult BuildAlreadyProcessed(
        string idempotencyKey,
        ProcessedEmailMessage existing)
    {
        string? ticketNumber = null;
        if (existing.TicketId is Guid ticketId)
        {
            ticketNumber = applicationDbContext.Tickets
                .FirstOrDefault(ticket => ticket.Id == ticketId)
                ?.TicketNumber;
        }

        return new InboundEmailItemResult(
            InboundEmailItemOutcome.AlreadyProcessed,
            idempotencyKey,
            ticketNumber,
            WasReopened: false,
            AcknowledgementSent: false,
            AcknowledgementFailed: false,
            FailureCode: null);
    }
}
