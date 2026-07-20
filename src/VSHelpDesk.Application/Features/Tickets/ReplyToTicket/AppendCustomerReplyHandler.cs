using VSHelpDesk.Application.Abstractions.Persistence;
using VSHelpDesk.Application.Common.Models;
using VSHelpDesk.Domain.Entities;
using VSHelpDesk.Domain.Enums;

namespace VSHelpDesk.Application.Features.Tickets.ReplyToTicket;

public sealed class AppendCustomerReplyHandler(
    IApplicationDbContext applicationDbContext,
    TimeProvider timeProvider)
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

        if (string.IsNullOrWhiteSpace(command.Content))
        {
            return Result.Failure<AppendCustomerReplyResult>("Content is required.");
        }

        var messageId = command.MessageId.Trim();
        var existing = applicationDbContext.ProcessedEmailMessages
            .FirstOrDefault(processed => processed.MessageId == messageId);
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

        var statusBefore = ticket.Status;
        var wasResolved = statusBefore == TicketStatus.Resolved;
        var originalSubject = ticket.Subject;
        var now = timeProvider.GetUtcNow().UtcDateTime;

        var message = new TicketMessage(
            ticket.Id,
            MessageSenderType.Customer,
            command.Content.Trim(),
            isHtml: command.IsHtml,
            userId: null,
            createdAtUtc: now);

        ticket.MarkAsCustomerReplied(now);
        // BR-021: subject must remain the original conversation subject.
        if (!string.Equals(originalSubject, ticket.Subject, StringComparison.Ordinal))
        {
            return Result.Failure<AppendCustomerReplyResult>("Ticket subject must remain immutable.");
        }

        var processed = new ProcessedEmailMessage(messageId, now, ticket.Id);
        applicationDbContext.Add(message);
        applicationDbContext.Add(processed);

        try
        {
            await applicationDbContext.SaveChangesAsync(cancellationToken);
        }
        catch (Exception)
        {
            var afterRace = applicationDbContext.ProcessedEmailMessages
                .FirstOrDefault(processedRow => processedRow.MessageId == messageId);
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
