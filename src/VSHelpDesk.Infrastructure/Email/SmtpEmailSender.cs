using MailKit.Net.Smtp;
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
        ArgumentNullException.ThrowIfNull(message);

        var options = emailOptions.Value;
        var from = CreateMailboxAddress(
            options.SupportMailboxDisplayName,
            options.SupportMailboxAddress,
            "support mailbox address");
        var to = CreateMailboxAddress(
            message.ToDisplayName,
            message.ToAddress,
            "recipient address");

        using var mime = new MimeMessage();
        mime.From.Add(from);
        mime.To.Add(to);
        mime.Subject = message.Subject ?? string.Empty;

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
        var secureSocket = MailTransportSecurity.ToSecureSocketOptions(options.SmtpSecurityMode);

        try
        {
            await client.ConnectAsync(options.SmtpHost, options.SmtpPort, secureSocket, cancellationToken);

            if (!string.IsNullOrWhiteSpace(options.SmtpUsername))
            {
                await client.AuthenticateAsync(
                    options.SmtpUsername,
                    options.SmtpPassword,
                    cancellationToken);
            }

            await client.SendAsync(mime, cancellationToken);
            await client.DisconnectAsync(true, cancellationToken);

            logger.LogInformation(
                "SMTP send succeeded host={SmtpHost} port={SmtpPort} subjectLength={SubjectLength}",
                options.SmtpHost,
                options.SmtpPort,
                message.Subject?.Length ?? 0);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "SMTP send failed host={SmtpHost} port={SmtpPort}",
                options.SmtpHost,
                options.SmtpPort);
            throw;
        }
    }

    private static MailboxAddress CreateMailboxAddress(
        string? displayName,
        string address,
        string label)
    {
        if (string.IsNullOrWhiteSpace(address))
        {
            throw new ArgumentException($"The {label} is required.", nameof(address));
        }

        if (ContainsControlCharacters(address))
        {
            throw new ArgumentException(
                $"The {label} contains invalid control characters.",
                nameof(address));
        }

        if (!string.IsNullOrEmpty(displayName) && ContainsControlCharacters(displayName))
        {
            throw new ArgumentException(
                $"The {label} display name contains invalid control characters.",
                nameof(displayName));
        }

        var trimmed = address.Trim();
        if (!MailboxAddress.TryParse(trimmed, out var parsed) ||
            !string.Equals(parsed.Address, trimmed, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"The {label} is not a valid mailbox address.",
                nameof(address));
        }

        return new MailboxAddress(
            string.IsNullOrWhiteSpace(displayName) ? string.Empty : displayName.Trim(),
            parsed.Address);
    }

    private static bool ContainsControlCharacters(string value)
    {
        foreach (var ch in value)
        {
            if (char.IsControl(ch))
            {
                return true;
            }
        }

        return false;
    }
}
