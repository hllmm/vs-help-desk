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
        var existing = applicationDbContext.ProcessedEmailMessages
            .FirstOrDefault(processed => processed.MessageId == command.MessageId);

        if (existing is not null)
        {
            // BR / UC-002: same Message-Id must not open another ticket or message.
            var existingTicket = applicationDbContext.Tickets
                .FirstOrDefault(ticket => ticket.Id == existing.TicketId);

            return Result.Success(new CreateTicketResult(
                existing.TicketId ?? existingTicket?.Id ?? Guid.Empty,
                existingTicket?.TicketNumber ?? string.Empty,
                FirstTicketMessageId: Guid.Empty,
                WasAlreadyProcessed: true));
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var ticketNumber = await ticketNumberGenerator.NextAsync(cancellationToken);
        var ticket = Ticket.Create(
            ticketNumber,
            command.Subject,
            command.CustomerName,
            command.CustomerEmail,
            now);

        var firstMessage = new TicketMessage(
            ticket.Id,
            MessageSenderType.Customer,
            command.Content,
            command.IsHtml);

        ticket.RecordMessageActivity(now);

        var processed = new ProcessedEmailMessage(command.MessageId, now, ticket.Id);

        applicationDbContext.Add(ticket);
        applicationDbContext.Add(firstMessage);
        applicationDbContext.Add(processed);
        await applicationDbContext.SaveChangesAsync(cancellationToken);

        return Result.Success(new CreateTicketResult(
            ticket.Id,
            ticket.TicketNumber,
            firstMessage.Id,
            WasAlreadyProcessed: false));
    }
}
