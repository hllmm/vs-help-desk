using VSHelpDesk.Application.Abstractions.Persistence;
using VSHelpDesk.Application.Common.Models;
using VSHelpDesk.Domain.Entities;
using VSHelpDesk.Domain.Enums;

namespace VSHelpDesk.Application.Features.Tickets.CreateTicket;

public sealed class CreateTicketHandler(
    IApplicationDbContext applicationDbContext,
    ITicketNumberGenerator ticketNumberGenerator,
    TimeProvider timeProvider)
{
    public async Task<Result<CreateTicketResult>> HandleAsync(
        CreateTicketCommand command,
        CancellationToken cancellationToken)
    {
        var validationError = Validate(command);
        if (validationError is not null)
        {
            return Result.Failure<CreateTicketResult>(validationError);
        }

        var messageId = command.MessageId.Trim();
        var existing = FindProcessed(messageId);
        if (existing is not null)
        {
            return Result.Success(BuildAlreadyProcessedResult(existing));
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var ticketNumber = await ticketNumberGenerator.NextAsync(cancellationToken);
        var ticket = Ticket.Create(
            ticketNumber,
            command.Subject.Trim(),
            command.CustomerName.Trim(),
            command.CustomerEmail.Trim(),
            now);

        var firstMessage = new TicketMessage(
            ticket.Id,
            MessageSenderType.Customer,
            command.Content.Trim(),
            command.IsHtml);

        ticket.RecordMessageActivity(now);

        var processed = new ProcessedEmailMessage(messageId, now, ticket.Id);

        applicationDbContext.Add(ticket);
        applicationDbContext.Add(firstMessage);
        applicationDbContext.Add(processed);

        try
        {
            await applicationDbContext.SaveChangesAsync(cancellationToken);
        }
        catch (Exception)
        {
            // Concurrent same MessageId: unique index wins; treat as idempotent success.
            var afterRace = FindProcessed(messageId);
            if (afterRace is not null)
            {
                return Result.Success(BuildAlreadyProcessedResult(afterRace));
            }

            throw;
        }

        return Result.Success(new CreateTicketResult(
            ticket.Id,
            ticket.TicketNumber,
            firstMessage.Id,
            WasAlreadyProcessed: false));
    }

    private ProcessedEmailMessage? FindProcessed(string messageId) =>
        applicationDbContext.ProcessedEmailMessages
            .FirstOrDefault(processed => processed.MessageId == messageId);

    private CreateTicketResult BuildAlreadyProcessedResult(ProcessedEmailMessage existing)
    {
        var existingTicket = existing.TicketId is null
            ? null
            : applicationDbContext.Tickets.FirstOrDefault(ticket => ticket.Id == existing.TicketId);

        var firstMessageId = existing.TicketId is null
            ? Guid.Empty
            : applicationDbContext.TicketMessages
                .Where(message => message.TicketId == existing.TicketId)
                .OrderBy(message => message.CreatedAt)
                .Select(message => message.Id)
                .FirstOrDefault();

        return new CreateTicketResult(
            existing.TicketId ?? existingTicket?.Id ?? Guid.Empty,
            existingTicket?.TicketNumber ?? string.Empty,
            firstMessageId,
            WasAlreadyProcessed: true);
    }

    private static string? Validate(CreateTicketCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.MessageId))
        {
            return "MessageId is required.";
        }

        if (string.IsNullOrWhiteSpace(command.Subject))
        {
            return "Subject is required.";
        }

        if (string.IsNullOrWhiteSpace(command.CustomerName))
        {
            return "CustomerName is required.";
        }

        if (string.IsNullOrWhiteSpace(command.CustomerEmail))
        {
            return "CustomerEmail is required.";
        }

        if (string.IsNullOrWhiteSpace(command.Content))
        {
            return "Content is required.";
        }

        return null;
    }
}
