using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using VSHelpDesk.Application.Abstractions.Email;

namespace VSHelpDesk.Infrastructure.Email;

public sealed class SmtpEmailSender(
    IOptions<EmailOptions> emailOptions,
    ILogger<SmtpEmailSender> logger) : IEmailSender
{
    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        var options = emailOptions.Value;
        using var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(
            options.SupportMailboxDisplayName,
            options.SupportMailboxAddress));
        mime.To.Add(new MailboxAddress(message.ToDisplayName, message.ToAddress));
        mime.Subject = message.Subject;

        var bodyBuilder = new BodyBuilder();
        if (message.IsHtml)
        {
            bodyBuilder.HtmlBody = message.Body;
        }
        else
        {
            bodyBuilder.TextBody = message.Body;
        }

        if (message.Attachments is { Count: > 0 })
        {
            foreach (var attachment in message.Attachments)
            {
                await using var stream = attachment.Content;
                await bodyBuilder.Attachments.AddAsync(
                    attachment.FileName,
                    stream,
                    ContentType.Parse(attachment.ContentType),
                    cancellationToken);
            }
        }

        mime.Body = bodyBuilder.ToMessageBody();

        using var client = new SmtpClient();
        var secureSocket = options.SmtpUseSsl
            ? SecureSocketOptions.StartTlsWhenAvailable
            : SecureSocketOptions.None;

        try
        {
            await client.ConnectAsync(options.SmtpHost, options.SmtpPort, secureSocket, cancellationToken);

            if (!string.IsNullOrWhiteSpace(options.Username))
            {
                await client.AuthenticateAsync(options.Username, options.Password, cancellationToken);
            }

            await client.SendAsync(mime, cancellationToken);
            await client.DisconnectAsync(true, cancellationToken);

            logger.LogInformation(
                "SMTP send succeeded host={SmtpHost} port={SmtpPort} to={ToAddress} subjectLength={SubjectLength}",
                options.SmtpHost,
                options.SmtpPort,
                message.ToAddress,
                message.Subject.Length);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "SMTP send failed host={SmtpHost} port={SmtpPort} to={ToAddress}",
                options.SmtpHost,
                options.SmtpPort,
                message.ToAddress);
            throw;
        }
    }
}
