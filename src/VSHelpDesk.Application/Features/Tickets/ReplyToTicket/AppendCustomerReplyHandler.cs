using VSHelpDesk.Application.Abstractions.Persistence;
using VSHelpDesk.Application.Common.Exceptions;
using VSHelpDesk.Application.Common.Models;
using VSHelpDesk.Application.Features.MailProcessing;
using VSHelpDesk.Domain.Entities;
using VSHelpDesk.Domain.Enums;

namespace VSHelpDesk.Application.Features.Tickets.ReplyToTicket;

public sealed class AppendCustomerReplyHandler(
    IApplicationDbContext applicationDbContext,
    TimeProvider timeProvider,
    IDatabaseErrorClassifier databaseErrorClassifier)
{
    public async Task<Result<AppendCustomerReplyResult>> HandleAsync(
        AppendCustomerReplyCommand command,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.IdempotencyKey))
        {
            return Result.Failure<AppendCustomerReplyResult>("IdempotencyKey is required.");
        }

        if (string.IsNullOrWhiteSpace(command.TicketNumber))
        {
            return Result.Failure<AppendCustomerReplyResult>("TicketNumber is required.");
        }

        try
        {
            return await AttemptOnceAsync(command, cancellationToken);
        }
        catch (Exception ex) when (databaseErrorClassifier.IsOptimisticConcurrencyConflict(ex))
        {
            // One safe retry with a fully reloaded ticket/message graph.
            applicationDbContext.ClearTrackedChanges();

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
        var existing = applicationDbContext.ProcessedEmailMessages
            .FirstOrDefault(processed => processed.IdempotencyKey == idempotencyKey);
        if (existing is not null)
        {
            return Result.Success(BuildAlreadyProcessedResult(existing, command.TicketNumber.Trim()));
        }

        var ticket = applicationDbContext.Tickets
            .FirstOrDefault(candidate => candidate.TicketNumber == command.TicketNumber.Trim());
        if (ticket is null)
        {
            return Result.Failure<AppendCustomerReplyResult>(
                $"Ticket '{command.TicketNumber}' was not found.");
        }

        // Optional From binding when caller supplies a customer address (ProcessIncoming).
        if (!string.IsNullOrWhiteSpace(command.FromAddress) &&
            !string.Equals(
                ticket.CustomerEmail.Trim(),
                command.FromAddress.Trim(),
                StringComparison.OrdinalIgnoreCase))
        {
            return Result.Failure<AppendCustomerReplyResult>(
                "From address does not match the ticket customer email.");
        }

        var statusBefore = ticket.Status;
        var wasResolved = statusBefore == TicketStatus.Resolved;
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var content = InboundMailLimits.NormalizeBody(command.Content);

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
        applicationDbContext.Add(message);
        applicationDbContext.Add(processed);

        try
        {
            await applicationDbContext.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            applicationDbContext.ClearTrackedChanges();

            if (databaseErrorClassifier.IsProcessedEmailIdempotencyConflict(ex))
            {
                var afterRace = applicationDbContext.ProcessedEmailMessages
                    .FirstOrDefault(processedRow => processedRow.IdempotencyKey == idempotencyKey);
                if (afterRace is not null)
                {
                    return Result.Success(BuildAlreadyProcessedResult(afterRace, ticket.TicketNumber));
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

    private AppendCustomerReplyResult BuildAlreadyProcessedResult(
        ProcessedEmailMessage existing,
        string fallbackTicketNumber)
    {
        var existingTicket = existing.TicketId is null
            ? null
            : applicationDbContext.Tickets.FirstOrDefault(ticket => ticket.Id == existing.TicketId);

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
