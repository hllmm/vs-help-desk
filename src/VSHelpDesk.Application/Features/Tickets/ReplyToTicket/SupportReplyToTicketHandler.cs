using Microsoft.Extensions.Logging;
using VSHelpDesk.Application.Abstractions.Authentication;
using VSHelpDesk.Application.Abstractions.Email;
using VSHelpDesk.Application.Abstractions.Persistence;
using VSHelpDesk.Application.Common.Exceptions;
using VSHelpDesk.Application.Common.Models;
using VSHelpDesk.Domain.Entities;
using VSHelpDesk.Domain.Enums;

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
            return Result.Failure<SupportReplyToTicketResult>("Content is required.");
        }

        var ticket = applicationDbContext.Tickets
            .FirstOrDefault(candidate => candidate.Id == command.TicketId);
        if (ticket is null)
        {
            throw new NotFoundException($"Ticket '{command.TicketId}' was not found.");
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var supportUserId = currentUserService.UserId;
        var message = new TicketMessage(
            ticket.Id,
            MessageSenderType.Support,
            command.Content.Trim(),
            isHtml: command.IsHtml,
            userId: supportUserId,
            createdAtUtc: now);

        // Persist first (BR-022): message survives SMTP failure.
        applicationDbContext.Add(message);
        ticket.RecordMessageActivity(now);
        await applicationDbContext.SaveChangesAsync(cancellationToken);

        var emailDelivered = false;
        string? emailError = null;
        try
        {
            await emailSender.SendAsync(
                new EmailMessage(
                    ToAddress: ticket.CustomerEmail,
                    ToDisplayName: ticket.CustomerName,
                    Subject: $"[{ticket.TicketNumber}] {ticket.Subject}",
                    Body: command.Content.Trim(),
                    IsHtml: command.IsHtml),
                cancellationToken);
            emailDelivered = true;

            ticket.MarkAsWaitingCustomerReply(now);
            await applicationDbContext.SaveChangesAsync(cancellationToken);

            logger.LogInformation(
                "Support reply delivered ticketNumber={TicketNumber} messageId={MessageId} to={ToAddress}",
                ticket.TicketNumber,
                message.Id,
                ticket.CustomerEmail);
        }
        catch (Exception ex)
        {
            emailError = "Email delivery failed; the support message was saved.";
            logger.LogError(
                ex,
                "Support reply SMTP failed after persist ticketNumber={TicketNumber} messageId={MessageId} to={ToAddress}",
                ticket.TicketNumber,
                message.Id,
                ticket.CustomerEmail);
        }

        return Result.Success(new SupportReplyToTicketResult(
            ticket.Id,
            ticket.TicketNumber,
            message.Id,
            ticket.Status.ToString(),
            emailDelivered,
            emailError));
    }
}
