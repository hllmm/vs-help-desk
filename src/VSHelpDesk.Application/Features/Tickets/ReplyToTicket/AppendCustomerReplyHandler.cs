using VSHelpDesk.Application.Abstractions.Persistence;
using VSHelpDesk.Application.Abstractions.Persistence.Repositories;
using VSHelpDesk.Application.Common.Exceptions;
using VSHelpDesk.Application.Common.Localization;
using VSHelpDesk.Application.Common.Models;
using VSHelpDesk.Application.Features.MailProcessing;
using VSHelpDesk.Domain.Entities;
using VSHelpDesk.Domain.Enums;
using VSHelpDesk.Application.Abstractions.Security;

namespace VSHelpDesk.Application.Features.Tickets.ReplyToTicket;

public sealed class AppendCustomerReplyHandler(
    ITicketRepository ticketRepository,
    IProcessedEmailRepository processedEmailRepository,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider,
    IDatabaseErrorClassifier databaseErrorClassifier,
    IHtmlSanitizerService? htmlSanitizerService = null,
    IMessageProvider? messages = null)
{
    public async Task<Result<AppendCustomerReplyResult>> HandleAsync(
        AppendCustomerReplyCommand command,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.IdempotencyKey))
        {
            return Result.Failure<AppendCustomerReplyResult>(
                messages?.Get(MessageKeys.Tickets.IdempotencyKeyRequired) ?? "IdempotencyKey zorunludur.");
        }

        if (string.IsNullOrWhiteSpace(command.TicketNumber))
        {
            return Result.Failure<AppendCustomerReplyResult>(
                messages?.Get(MessageKeys.Tickets.TicketNumberRequired) ?? "Ticket numarası (TicketNumber) zorunludur.");
        }

        try
        {
            return await AttemptOnceAsync(command, cancellationToken);
        }
        catch (Exception ex) when (databaseErrorClassifier.IsOptimisticConcurrencyConflict(ex))
        {
            // One safe retry with a fully reloaded ticket/message graph.
            unitOfWork.ClearTrackedChanges();

            try
            {
                return await AttemptOnceAsync(command, cancellationToken);
            }
            catch (Exception retryEx) when (databaseErrorClassifier.IsOptimisticConcurrencyConflict(retryEx))
            {
                throw new OptimisticConcurrencyException(
                    "Could not append customer reply due to a concurrent update.",
                    retryEx);
            }
        }
    }

    private async Task<Result<AppendCustomerReplyResult>> AttemptOnceAsync(
        AppendCustomerReplyCommand command,
        CancellationToken cancellationToken)
    {
        var idempotencyKey = command.IdempotencyKey.Trim();
        var existing = await processedEmailRepository.GetByIdempotencyKeyAsync(idempotencyKey, cancellationToken);
        if (existing is not null)
        {
            return Result.Success(await BuildAlreadyProcessedResultAsync(existing, command.TicketNumber.Trim(), cancellationToken));
        }

        var ticket = await ticketRepository.GetByNumberAsync(command.TicketNumber.Trim(), cancellationToken);
        if (ticket is null)
        {
            return Result.Failure<AppendCustomerReplyResult>(
                messages?.Get(MessageKeys.Tickets.NotFound, command.TicketNumber) ?? $"'{command.TicketNumber}' numaralı Ticket bulunamadı.");
        }

        // Optional From binding when caller supplies a customer address (ProcessIncoming).
        if (!string.IsNullOrWhiteSpace(command.FromAddress) &&
            !string.Equals(
                ticket.CustomerEmail.Trim(),
                command.FromAddress.Trim(),
                StringComparison.OrdinalIgnoreCase))
        {
            return Result.Failure<AppendCustomerReplyResult>(
                messages?.Get(MessageKeys.Tickets.FromAddressMismatch) ?? "Gönderen adresi, Ticket müşteri e-postasıyla eşleşmiyor.");
        }

        var statusBefore = ticket.Status;
        var wasResolved = statusBefore == TicketStatus.Resolved;
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var sanitizedContent = htmlSanitizerService is not null
            ? htmlSanitizerService.SanitizeHtml(command.Content)
            : command.Content;
        var content = InboundMailLimits.NormalizeBody(sanitizedContent);

        // BR-004 / BR-013: append the customer reply to the matched conversation;
        // never replace or overwrite an earlier message.
        var message = new TicketMessage(
            ticket.Id,
            MessageSenderType.Customer,
            content,
            isHtml: false,
            userId: null,
            createdAtUtc: now);

        ticket.MarkAsCustomerReplied(now);

        var processed = ProcessedEmailMessage.ForAppendedReply(
            idempotencyKey,
            sourceMessageId: command.SourceMessageId,
            processedAtUtc: now,
            ticketId: ticket.Id);
        await ticketRepository.AddMessageAsync(message, cancellationToken);
        await processedEmailRepository.AddAsync(processed, cancellationToken);

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            unitOfWork.ClearTrackedChanges();

            if (databaseErrorClassifier.IsProcessedEmailIdempotencyConflict(ex))
            {
                var afterRace = await processedEmailRepository.GetByIdempotencyKeyAsync(idempotencyKey, cancellationToken);
                if (afterRace is not null)
                {
                    return Result.Success(await BuildAlreadyProcessedResultAsync(afterRace, ticket.TicketNumber, cancellationToken));
                }
            }

            throw;
        }

        return Result.Success(new AppendCustomerReplyResult(
            ticket.Id,
            ticket.TicketNumber,
            message.Id,
            statusBefore,
            ticket.Status,
            WasAlreadyProcessed: false,
            WasReopened: wasResolved));
    }

    private async Task<AppendCustomerReplyResult> BuildAlreadyProcessedResultAsync(
        ProcessedEmailMessage existing,
        string fallbackTicketNumber,
        CancellationToken cancellationToken)
    {
        var existingTicket = existing.TicketId is null
            ? null
            : await ticketRepository.GetByIdAsync(existing.TicketId.Value, cancellationToken: cancellationToken);

        return new AppendCustomerReplyResult(
            existing.TicketId ?? existingTicket?.Id ?? Guid.Empty,
            existingTicket?.TicketNumber ?? fallbackTicketNumber,
            Guid.Empty,
            existingTicket?.Status ?? TicketStatus.CustomerReplied,
            existingTicket?.Status ?? TicketStatus.CustomerReplied,
            WasAlreadyProcessed: true,
            WasReopened: false);
    }
}
