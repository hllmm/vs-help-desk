using Microsoft.Extensions.Logging;
using VSHelpDesk.Application.Abstractions.Email;
using VSHelpDesk.Application.Abstractions.Persistence;
using VSHelpDesk.Application.Abstractions.Persistence.Repositories;
using VSHelpDesk.Application.Abstractions.Storage;
using VSHelpDesk.Application.Common.Exceptions;
using VSHelpDesk.Application.Features.MailProcessing.Acknowledgements;
using VSHelpDesk.Application.Features.Tickets.CreateTicket;
using VSHelpDesk.Application.Features.Tickets.ReplyToTicket;
using VSHelpDesk.Domain.Entities;
using VSHelpDesk.Domain.Tickets;

using VSHelpDesk.Application.Abstractions.Security;

namespace VSHelpDesk.Application.Features.MailProcessing.ProcessIncomingEmails;

/// <summary>
/// Processes a single inbound receipt inside one DI/DbContext scope.
/// Normalization runs before any entity is tracked.
/// </summary>
public sealed class InboundEmailItemProcessor(
    IProcessedEmailRepository processedEmailRepository,
    ITicketRepository ticketRepository,
    IUnitOfWork unitOfWork,
    CreateTicketHandler createTicketHandler,
    AppendCustomerReplyHandler appendCustomerReplyHandler,
    AcknowledgementDispatcher acknowledgementDispatcher,
    ITicketAttachmentWriter ticketAttachmentWriter,
    TimeProvider timeProvider,
    IDatabaseErrorClassifier databaseErrorClassifier,
    ILogger<InboundEmailItemProcessor> logger,
    IHtmlSanitizerService? htmlSanitizerService = null) : IInboundEmailItemProcessor
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

        var existing = await processedEmailRepository.GetByIdempotencyKeyAsync(identity.IdempotencyKey, cancellationToken);
        if (existing is not null)
        {
            return await BuildAlreadyProcessedAsync(identity.IdempotencyKey, existing, cancellationToken);
        }

        try
        {
            if (TicketNumberParser.TryFindInText(normalized.Subject, out var ticketNumber) &&
                await TryGetMatchingCustomerTicketAsync(ticketNumber, normalized.FromAddress, cancellationToken))
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
        var existing = await processedEmailRepository.GetByIdempotencyKeyAsync(identity.IdempotencyKey, cancellationToken);
        if (existing is not null)
        {
            return await BuildAlreadyProcessedAsync(identity.IdempotencyKey, existing, cancellationToken);
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        await processedEmailRepository.AddAsync(ProcessedEmailMessage.ForQuarantine(
            identity.IdempotencyKey,
            sourceMessageId: identity.SourceMessageId,
            processedAtUtc: now,
            processingNote: processingNote), cancellationToken);

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            unitOfWork.ClearTrackedChanges();

            if (databaseErrorClassifier.IsProcessedEmailIdempotencyConflict(ex))
            {
                var afterRace = await processedEmailRepository.GetByIdempotencyKeyAsync(identity.IdempotencyKey, cancellationToken);
                if (afterRace is not null)
                {
                    return await BuildAlreadyProcessedAsync(identity.IdempotencyKey, afterRace, cancellationToken);
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
        var sanitizedBody = htmlSanitizerService is not null
            ? htmlSanitizerService.SanitizeHtml(normalized.Body)
            : normalized.Body;
        var createResult = await createTicketHandler.HandleAsync(
            new CreateTicketCommand(
                IdempotencyKey: normalized.IdempotencyKey,
                SourceMessageId: normalized.SourceMessageId,
                Subject: normalized.Subject,
                CustomerName: normalized.FromDisplayName,
                CustomerEmail: normalized.FromAddress,
                Content: sanitizedBody),
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

        logger.LogInformation(
            "Inbound ticket created ticketId={TicketId} ticketNumber={TicketNumber} status={Status}",
            created.TicketId,
            created.TicketNumber,
            "New");

        await PersistAttachmentsAsync(
            created.FirstTicketMessageId,
            normalized.Attachments,
            cancellationToken);

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
        var sanitizedBody = htmlSanitizerService is not null
            ? htmlSanitizerService.SanitizeHtml(normalized.Body)
            : normalized.Body;
        var replyResult = await appendCustomerReplyHandler.HandleAsync(
            new AppendCustomerReplyCommand(
                IdempotencyKey: normalized.IdempotencyKey,
                SourceMessageId: normalized.SourceMessageId,
                TicketNumber: ticketNumber,
                Content: sanitizedBody,
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

        logger.LogInformation(
            "Inbound customer reply appended ticketId={TicketId} ticketNumber={TicketNumber} messageId={MessageId} oldStatus={OldStatus} newStatus={NewStatus} reopened={Reopened}",
            reply.TicketId,
            reply.TicketNumber,
            reply.MessageId,
            reply.StatusBefore,
            reply.StatusAfter,
            reply.WasReopened);

        await PersistAttachmentsAsync(
            reply.MessageId,
            normalized.Attachments,
            cancellationToken);

        return new InboundEmailItemResult(
            InboundEmailItemOutcome.AppendedReply,
            normalized.IdempotencyKey,
            reply.TicketNumber,
            WasReopened: reply.WasReopened,
            AcknowledgementSent: false,
            AcknowledgementFailed: false,
            FailureCode: null);
    }

    private async Task PersistAttachmentsAsync(
        Guid ticketMessageId,
        IReadOnlyList<IncomingEmailAttachment> attachments,
        CancellationToken cancellationToken)
    {
        if (attachments.Count == 0)
        {
            return;
        }

        foreach (var attachment in attachments)
        {
            try
            {
                await using var stream = new MemoryStream(
                    attachment.Content ?? Array.Empty<byte>(),
                    writable: false);
                var result = await ticketAttachmentWriter.TryWriteAsync(
                    ticketMessageId,
                    attachment.FileName,
                    attachment.ContentType,
                    stream,
                    attachment.FileSize > 0 ? attachment.FileSize : stream.Length,
                    cancellationToken);

                if (!result.WasStored)
                {
                    logger.LogWarning(
                        "Skipped inbound attachment fileName={FileName} messageId={MessageId} reason={Reason}",
                        attachment.FileName,
                        ticketMessageId,
                        result.SkipReason);
                }
            }
            catch (Exception ex)
            {
                // Never fail the mail item solely because one attachment could not be stored.
                logger.LogError(
                    ex,
                    "Unexpected error storing inbound attachment fileName={FileName} messageId={MessageId}",
                    attachment.FileName,
                    ticketMessageId);
            }
        }
    }

    private async Task<bool> TryGetMatchingCustomerTicketAsync(
        string ticketNumber,
        string fromAddress,
        CancellationToken cancellationToken)
    {
        var found = await ticketRepository.GetByNumberAsync(ticketNumber, cancellationToken);

        if (found is null ||
            !string.Equals(
                found.CustomerEmail.Trim(),
                fromAddress,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    private async Task<InboundEmailItemResult> BuildAlreadyProcessedAsync(
        string idempotencyKey,
        ProcessedEmailMessage existing,
        CancellationToken cancellationToken)
    {
        string? ticketNumber = null;
        if (existing.TicketId is Guid ticketId)
        {
            var ticket = await ticketRepository.GetByIdAsync(ticketId, cancellationToken: cancellationToken);
            ticketNumber = ticket?.TicketNumber;
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
