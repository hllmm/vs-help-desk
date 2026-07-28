using Microsoft.Extensions.Logging;
using VSHelpDesk.Application.Abstractions.Authentication;
using VSHelpDesk.Application.Abstractions.Email;
using VSHelpDesk.Application.Abstractions.Persistence;
using VSHelpDesk.Application.Common.Exceptions;
using VSHelpDesk.Application.Common.Models;
using VSHelpDesk.Domain.Entities;
using VSHelpDesk.Domain.Enums;
using VSHelpDesk.Domain.Exceptions;

namespace VSHelpDesk.Application.Features.Tickets.ReplyToTicket;

public sealed class SupportReplyToTicketHandler(
    IApplicationDbContext applicationDbContext,
    IEmailSender emailSender,
    ICurrentUserService currentUserService,
    TimeProvider timeProvider,
    ILogger<SupportReplyToTicketHandler> logger)
{
    public async Task<Result<SupportReplyToTicketResult>> HandleAsync(
        SupportReplyToTicketCommand command,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.Content))
        {
            return Result.Failure<SupportReplyToTicketResult>(
                SupportReplyCodes.ContentRequired);
        }

        var content = command.Content.Trim();
        if (content.Length > SupportReplyLimits.MaxContentLength)
        {
            return Result.Failure<SupportReplyToTicketResult>(
                SupportReplyCodes.ContentTooLong);
        }

        var ticket = applicationDbContext.Tickets
            .FirstOrDefault(candidate => candidate.Id == command.TicketId);
        if (ticket is null)
        {
            throw new NotFoundException($"Ticket '{command.TicketId}' was not found.");
        }

        if (ticket.Status == TicketStatus.Resolved)
        {
            throw new ResolvedTicketReplyException();
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var supportUserId = currentUserService.UserId;
        var message = new TicketMessage(
            ticket.Id,
            MessageSenderType.Support,
            content,
            isHtml: false,
            userId: supportUserId,
            createdAtUtc: now);

        // Persist first (BR-022): message survives SMTP failure.
        applicationDbContext.Add(message);
        ticket.RecordMessageActivity(now);
        await applicationDbContext.SaveChangesAsync(cancellationToken);

        try
        {
            await emailSender.SendAsync(
                new EmailMessage(
                    ToAddress: ticket.CustomerEmail,
                    ToDisplayName: ticket.CustomerName,
                    Subject: $"{ticket.ReplyReference} {ticket.Subject}",
                    Body: content,
                    IsHtml: false),
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Support reply SMTP failed after persistence ticketId={TicketId} messageId={MessageId}",
                ticket.Id,
                message.Id);

            return Result.Success(new SupportReplyToTicketResult(
                ticket.Id,
                ticket.TicketNumber,
                message.Id,
                ticket.Status.ToString(),
                EmailDelivered: false,
                TicketStateUpdated: false,
                NoticeCode: SupportReplyCodes.SmtpDeliveryFailed));
        }

        var state = await TryMarkWaitingAsync(
            ticket,
            now,
            message.Id,
            cancellationToken);

        return Result.Success(new SupportReplyToTicketResult(
            ticket.Id,
            ticket.TicketNumber,
            message.Id,
            state.ConfirmedStatus.ToString(),
            EmailDelivered: true,
            TicketStateUpdated: state.Updated,
            NoticeCode: state.Updated
                ? null
                : SupportReplyCodes.TicketStateConflict));
    }

    private async Task<(bool Updated, TicketStatus ConfirmedStatus)>
        TryMarkWaitingAsync(
            Ticket ticket,
            DateTime now,
            Guid messageId,
            CancellationToken cancellationToken)
    {
        // Status confirmed by the first message/activity commit (before waiting-state mutation).
        var statusAfterMessageCommit = ticket.Status;

        try
        {
            if (!CanMarkAsWaitingCustomerReply(ticket.Status))
            {
                return LogWaitingConflict(ticket.Id, messageId, ticket.Status);
            }

            var oldStatus = ticket.Status;
            ticket.MarkAsWaitingCustomerReply(now);
            await applicationDbContext.SaveChangesAsync(cancellationToken);
            return LogWaitingSuccess(
                ticket.Id,
                messageId,
                oldStatus,
                TicketStatus.WaitingCustomerReply);
        }
        catch (OptimisticConcurrencyException)
        {
            applicationDbContext.ClearTrackedChanges();
        }
        catch (DomainException)
        {
            // Defensive: concurrent close/reopen left an illegal transition on the tracked entity.
            return LogWaitingConflict(ticket.Id, messageId, statusAfterMessageCommit);
        }

        var reloaded = applicationDbContext.Tickets
            .FirstOrDefault(candidate => candidate.Id == ticket.Id);
        if (reloaded is null)
        {
            throw new NotFoundException($"Ticket '{ticket.Id}' was not found.");
        }

        // Concurrent resolve (or other illegal transition) after SMTP: message already saved and
        // emailed — return ticket-state-conflict, never DomainException → HTTP 400.
        if (!CanMarkAsWaitingCustomerReply(reloaded.Status))
        {
            return LogWaitingConflict(ticket.Id, messageId, reloaded.Status);
        }

        try
        {
            var oldStatus = reloaded.Status;
            reloaded.MarkAsWaitingCustomerReply(now);
            await applicationDbContext.SaveChangesAsync(cancellationToken);
            return LogWaitingSuccess(
                reloaded.Id,
                messageId,
                oldStatus,
                reloaded.Status);
        }
        catch (OptimisticConcurrencyException)
        {
            applicationDbContext.ClearTrackedChanges();

            var confirmedStatus = applicationDbContext.Tickets
                .Where(candidate => candidate.Id == ticket.Id)
                .Select(candidate => (TicketStatus?)candidate.Status)
                .FirstOrDefault()
                ?? statusAfterMessageCommit;

            return LogWaitingConflict(ticket.Id, messageId, confirmedStatus);
        }
        catch (DomainException)
        {
            applicationDbContext.ClearTrackedChanges();

            var confirmedStatus = applicationDbContext.Tickets
                .Where(candidate => candidate.Id == ticket.Id)
                .Select(candidate => (TicketStatus?)candidate.Status)
                .FirstOrDefault()
                ?? statusAfterMessageCommit;

            return LogWaitingConflict(ticket.Id, messageId, confirmedStatus);
        }
    }

    /// <summary>
    /// Same allowed sources as <see cref="Ticket.MarkAsWaitingCustomerReply"/>.
    /// </summary>
    private static bool CanMarkAsWaitingCustomerReply(TicketStatus status) =>
        status is TicketStatus.New
            or TicketStatus.CustomerReplied
            or TicketStatus.WaitingCustomerReply;

    private (bool Updated, TicketStatus ConfirmedStatus) LogWaitingConflict(
        Guid ticketId,
        Guid messageId,
        TicketStatus confirmedStatus)
    {
        logger.LogWarning(
            "Support reply waiting-state conflict after SMTP ticketId={TicketId} messageId={MessageId} status={Status}",
            ticketId,
            messageId,
            confirmedStatus);

        return (false, confirmedStatus);
    }

    private (bool Updated, TicketStatus ConfirmedStatus) LogWaitingSuccess(
        Guid ticketId,
        Guid messageId,
        TicketStatus oldStatus,
        TicketStatus newStatus)
    {
        logger.LogInformation(
            "Support reply state updated ticketId={TicketId} messageId={MessageId} oldStatus={OldStatus} newStatus={NewStatus}",
            ticketId,
            messageId,
            oldStatus,
            newStatus);

        return (true, newStatus);
    }
}
