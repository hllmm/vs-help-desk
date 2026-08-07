using System.Net.Mail;
using VSHelpDesk.Application.Abstractions.Authentication;
using VSHelpDesk.Application.Abstractions.Persistence;
using VSHelpDesk.Application.Abstractions.Persistence.Repositories;
using VSHelpDesk.Application.Abstractions.Security;
using VSHelpDesk.Application.Common.Models;
using VSHelpDesk.Application.Features.MailProcessing;
using VSHelpDesk.Application.Features.Tickets.CreateTicket;
using VSHelpDesk.Domain.Entities;

namespace VSHelpDesk.Application.Features.Tickets.CreatePortalTicket;

public sealed class CreatePortalTicketHandler(
    IPortalTicketRequestRepository portalTicketRequestRepository,
    ITicketRepository ticketRepository,
    IUnitOfWork unitOfWork,
    ITicketNumberGenerator ticketNumberGenerator,
    TimeProvider timeProvider,
    ICurrentUserService currentUserService,
    IDatabaseErrorClassifier databaseErrorClassifier)
{
    public async Task<Result<CreatePortalTicketResult>> HandleAsync(
        CreatePortalTicketCommand command,
        CancellationToken cancellationToken)
    {
        var validationError = Validate(command);
        if (validationError is not null)
        {
            return Result.Failure<CreatePortalTicketResult>(validationError);
        }

        if (currentUserService.UserId is not { } userId)
        {
            return Result.Failure<CreatePortalTicketResult>(CreatePortalTicketErrorCodes.UserRequired);
        }

        if (!Guid.TryParse(command.IdempotencyKey, out var keyGuid))
        {
            return Result.Failure<CreatePortalTicketResult>(CreatePortalTicketErrorCodes.IdempotencyKeyInvalid);
        }

        var normalizedKey = keyGuid.ToString("D");
        var normalizedSubject = command.Subject.Trim();
        var normalizedCustomerName = command.CustomerName.Trim();
        var normalizedCustomerEmail = command.CustomerEmail.Trim();
        var normalizedContent = command.Content.Trim();
        var requestHash = PortalTicketRequestHash.Compute(
            normalizedSubject,
            normalizedCustomerName,
            normalizedCustomerEmail,
            normalizedContent);

        var existing = await portalTicketRequestRepository.GetByUserAndKeyAsync(
            userId,
            normalizedKey,
            cancellationToken);
        if (existing is not null)
        {
            return await ReplayOrConflictAsync(existing, requestHash, cancellationToken);
        }

        var draft = await TicketDraftFactory.CreateAsync(
            ticketNumberGenerator,
            timeProvider,
            normalizedSubject,
            normalizedCustomerName,
            normalizedCustomerEmail,
            normalizedContent,
            cancellationToken);
        var portalRequest = PortalTicketRequest.Create(
            userId,
            normalizedKey,
            requestHash,
            draft.Ticket.Id,
            draft.CreatedAtUtc);

        await ticketRepository.AddAsync(draft.Ticket, cancellationToken);
        await ticketRepository.AddMessageAsync(draft.FirstMessage, cancellationToken);
        await portalTicketRequestRepository.AddAsync(portalRequest, cancellationToken);

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            unitOfWork.ClearTrackedChanges();

            if (databaseErrorClassifier.IsPortalTicketRequestIdempotencyConflict(ex))
            {
                var afterRace = await portalTicketRequestRepository.GetByUserAndKeyAsync(
                    userId,
                    normalizedKey,
                    cancellationToken);
                if (afterRace is not null)
                {
                    return await ReplayOrConflictAsync(afterRace, requestHash, cancellationToken);
                }
            }

            throw;
        }

        return Result.Success(new CreatePortalTicketResult(
            draft.Ticket.Id,
            draft.Ticket.TicketNumber,
            draft.FirstMessage.Id,
            WasAlreadyProcessed: false));
    }

    private async Task<Result<CreatePortalTicketResult>> ReplayOrConflictAsync(
        PortalTicketRequest existing,
        string requestHash,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(existing.RequestHash, requestHash, StringComparison.Ordinal))
        {
            return Result.Failure<CreatePortalTicketResult>(
                CreatePortalTicketErrorCodes.PayloadConflict);
        }

        var ticket = await ticketRepository.GetByIdAsync(existing.TicketId, cancellationToken);
        if (ticket is null)
        {
            throw new InvalidOperationException(
                "Portal idempotency state references a missing ticket.");
        }

        var firstMessageId = await ticketRepository.GetFirstMessageIdAsync(
            existing.TicketId,
            cancellationToken);
        return Result.Success(new CreatePortalTicketResult(
            ticket.Id,
            ticket.TicketNumber,
            firstMessageId,
            WasAlreadyProcessed: true));
    }

    private static string? Validate(
        CreatePortalTicketCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.IdempotencyKey))
        {
            return CreatePortalTicketErrorCodes.IdempotencyKeyRequired;
        }

        if (!Guid.TryParse(command.IdempotencyKey, out _))
        {
            return CreatePortalTicketErrorCodes.IdempotencyKeyInvalid;
        }

        if (string.IsNullOrWhiteSpace(command.Subject))
        {
            return CreatePortalTicketErrorCodes.SubjectRequired;
        }

        if (string.IsNullOrWhiteSpace(command.CustomerName))
        {
            return CreatePortalTicketErrorCodes.CustomerNameRequired;
        }

        if (string.IsNullOrWhiteSpace(command.CustomerEmail))
        {
            return CreatePortalTicketErrorCodes.CustomerEmailRequired;
        }

        if (string.IsNullOrWhiteSpace(command.Content))
        {
            return CreatePortalTicketErrorCodes.ContentRequired;
        }

        if (command.Subject.Trim().Length > InboundMailLimits.MaxSubjectLength ||
            command.CustomerName.Trim().Length > InboundMailLimits.MaxDisplayNameLength ||
            command.CustomerEmail.Trim().Length > InboundMailLimits.MaxAddressLength)
        {
            return CreatePortalTicketErrorCodes.InvalidPayload;
        }

        var normalizedEmail = command.CustomerEmail.Trim();
        if (!MailAddress.TryCreate(normalizedEmail, out var parsedEmail) ||
            !string.Equals(
                parsedEmail.Address,
                normalizedEmail,
                StringComparison.OrdinalIgnoreCase))
        {
            return CreatePortalTicketErrorCodes.CustomerEmailInvalid;
        }

        if (command.Content.Trim().Length > PortalTicketLimits.MaxContentLength)
        {
            return CreatePortalTicketErrorCodes.ContentTooLong;
        }

        return null;
    }
}
