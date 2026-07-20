using VSHelpDesk.Application.Abstractions.Persistence;
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
        if (string.IsNullOrWhiteSpace(command.MessageId))
        {
            return Result.Failure<AppendCustomerReplyResult>("MessageId is required.");
        }

        if (string.IsNullOrWhiteSpace(command.TicketNumber))
        {
            return Result.Failure<AppendCustomerReplyResult>("TicketNumber is required.");
        }

        var idempotencyKey = command.MessageId.Trim();
        var existing = applicationDbContext.ProcessedEmailMessages
            .FirstOrDefault(processed => processed.IdempotencyKey == idempotencyKey);
        if (existing is not null)
        {
            var existingTicket = existing.TicketId is null
                ? null
                : applicationDbContext.Tickets.FirstOrDefault(ticket => ticket.Id == existing.TicketId);

            return Result.Success(new AppendCustomerReplyResult(
                existing.TicketId ?? existingTicket?.Id ?? Guid.Empty,
                existingTicket?.TicketNumber ?? command.TicketNumber.Trim(),
                Guid.Empty,
                existingTicket?.Status ?? TicketStatus.CustomerReplied,
                existingTicket?.Status ?? TicketStatus.CustomerReplied,
                WasAlreadyProcessed: true,
                WasReopened: false));
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
            sourceMessageId: idempotencyKey,
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
                    return Result.Success(new AppendCustomerReplyResult(
                        afterRace.TicketId ?? ticket.Id,
                        ticket.TicketNumber,
                        Guid.Empty,
                        statusBefore,
                        ticket.Status,
                        WasAlreadyProcessed: true,
                        WasReopened: false));
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
}
