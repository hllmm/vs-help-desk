using Microsoft.Extensions.Logging;
using VSHelpDesk.Application.Abstractions.Email;
using VSHelpDesk.Application.Common.Models;

namespace VSHelpDesk.Application.Features.MailProcessing.ProcessIncomingEmails;

public sealed class ProcessIncomingEmailsHandler(
    IEmailReceiver emailReceiver,
    IEmailSender emailSender,
    IEmailBoundarySettings emailBoundarySettings,
    ILogger<ProcessIncomingEmailsHandler> logger)
{
    public async Task<Result<ProcessIncomingEmailsResult>> HandleAsync(
        ProcessIncomingEmailsCommand command,
        CancellationToken cancellationToken)
    {
        _ = command;
        var mode = emailBoundarySettings.ReceiverMode;

        logger.LogInformation(
            "ProcessIncomingEmails started receiverMode={ReceiverMode}",
            mode);

        IReadOnlyList<IncomingEmail> unread;
        try
        {
            unread = await emailReceiver.FetchUnreadAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "ProcessIncomingEmails fetch failed receiverMode={ReceiverMode}",
                mode);
            return Result.Failure<ProcessIncomingEmailsResult>(
                "Failed to fetch unread emails from the configured receiver.");
        }

        var messageIds = unread
            .Select(message => message.MessageId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToArray();

        logger.LogInformation(
            "ProcessIncomingEmails fetched count={Count} messageIds={MessageIds} receiverMode={ReceiverMode}",
            unread.Count,
            string.Join(',', messageIds),
            mode);

        var probeSent = false;
        if (emailBoundarySettings.SendSmtpProbeOnProcessJob)
        {
            try
            {
                await emailSender.SendAsync(
                    new EmailMessage(
                        ToAddress: emailBoundarySettings.SupportMailboxAddress,
                        ToDisplayName: emailBoundarySettings.SupportMailboxDisplayName,
                        Subject: "[VSHelpDesk] SMTP probe from process-incoming-emails",
                        Body: "SMTP connectivity probe (Day 8). No customer content.",
                        IsHtml: false),
                    cancellationToken);
                probeSent = true;
                logger.LogInformation(
                    "ProcessIncomingEmails SMTP probe sent to={ToAddress}",
                    emailBoundarySettings.SupportMailboxAddress);
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "ProcessIncomingEmails SMTP probe failed to={ToAddress}",
                    emailBoundarySettings.SupportMailboxAddress);
                return Result.Failure<ProcessIncomingEmailsResult>(
                    "SMTP probe failed; check Email:SmtpHost/SmtpPort and Mailpit.");
            }
        }

        return Result.Success(new ProcessIncomingEmailsResult(
            mode,
            unread.Count,
            messageIds,
            probeSent));
    }
}
