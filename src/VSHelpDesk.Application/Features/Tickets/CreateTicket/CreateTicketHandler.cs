using VSHelpDesk.Application.Abstractions.Persistence;
using VSHelpDesk.Application.Abstractions.Persistence.Repositories;
using VSHelpDesk.Application.Common;
using VSHelpDesk.Application.Common.Models;
using VSHelpDesk.Application.Features.MailProcessing;
using VSHelpDesk.Domain.Entities;
using VSHelpDesk.Domain.Enums;

using VSHelpDesk.Application.Abstractions.Security;

namespace VSHelpDesk.Application.Features.Tickets.CreateTicket;

public sealed class CreateTicketHandler(
    ITicketRepository ticketRepository,
    IProcessedEmailRepository processedEmailRepository,
    IUnitOfWork unitOfWork,
    ITicketNumberGenerator ticketNumberGenerator,
    TimeProvider timeProvider,
    IDatabaseErrorClassifier databaseErrorClassifier,
    IHtmlSanitizerService? htmlSanitizerService = null)
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

        var idempotencyKey = command.IdempotencyKey.Trim();
        var existing = await processedEmailRepository.GetByIdempotencyKeyAsync(idempotencyKey, cancellationToken);
        if (existing is not null)
        {
            return Result.Success(await BuildAlreadyProcessedResultAsync(existing, cancellationToken));
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var ticketNumber = await ticketNumberGenerator.NextAsync(cancellationToken);
        var ticket = Ticket.Create(
            ticketNumber,
            command.Subject.Trim(),
            command.CustomerName.Trim(),
            command.CustomerEmail.Trim(),
            now);

        // Inbound mail: store sanitized plain text/HTML body.
        var sanitizedContent = htmlSanitizerService is not null
            ? htmlSanitizerService.SanitizeHtml(command.Content)
            : command.Content;
        var content = InboundMailLimits.NormalizeBody(sanitizedContent);
        var firstMessage = new TicketMessage(
            ticket.Id,
            MessageSenderType.Customer,
            content,
            isHtml: false,
            userId: null,
            createdAtUtc: now);

        ticket.RecordMessageActivity(now);

        var processed = ProcessedEmailMessage.ForCreatedTicket(
            idempotencyKey,
            sourceMessageId: command.SourceMessageId,
            processedAtUtc: now,
            ticketId: ticket.Id);

        await ticketRepository.AddAsync(ticket, cancellationToken);
        await ticketRepository.AddMessageAsync(firstMessage, cancellationToken);
        await processedEmailRepository.AddAsync(processed, cancellationToken);

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            // Drop failed graph so later mails in the same scoped context can save.
            unitOfWork.ClearTrackedChanges();

            if (databaseErrorClassifier.IsProcessedEmailIdempotencyConflict(ex))
            {
                var afterRace = await processedEmailRepository.GetByIdempotencyKeyAsync(idempotencyKey, cancellationToken);
                if (afterRace is not null)
                {
                    return Result.Success(await BuildAlreadyProcessedResultAsync(afterRace, cancellationToken));
                }
            }

            throw;
        }

        return Result.Success(new CreateTicketResult(
            ticket.Id,
            ticket.TicketNumber,
            firstMessage.Id,
            processed.Id,
            WasAlreadyProcessed: false));
    }

    private async Task<CreateTicketResult> BuildAlreadyProcessedResultAsync(
        ProcessedEmailMessage existing,
        CancellationToken cancellationToken)
    {
        var existingTicket = existing.TicketId is null
            ? null
            : await ticketRepository.GetByIdAsync(existing.TicketId.Value, cancellationToken: cancellationToken);

        var firstMessageId = existing.TicketId is null
            ? Guid.Empty
            : await ticketRepository.GetFirstMessageIdAsync(existing.TicketId.Value, cancellationToken);

        return new CreateTicketResult(
            existing.TicketId ?? existingTicket?.Id ?? Guid.Empty,
            existingTicket?.TicketNumber ?? string.Empty,
            firstMessageId,
            existing.Id,
            WasAlreadyProcessed: true);
    }

    private static string? Validate(CreateTicketCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.IdempotencyKey))
        {
            return ApplicationMessages.Tickets.IdempotencyKeyRequired;
        }

        if (string.IsNullOrWhiteSpace(command.Subject))
        {
            return ApplicationMessages.Tickets.SubjectRequired;
        }

        if (string.IsNullOrWhiteSpace(command.CustomerName))
        {
            return ApplicationMessages.Tickets.CustomerNameRequired;
        }

        if (string.IsNullOrWhiteSpace(command.CustomerEmail))
        {
            return ApplicationMessages.Tickets.CustomerEmailRequired;
        }

        return null;
    }
}
