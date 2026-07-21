using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MimeKit;
using VSHelpDesk.Application.Abstractions.Email;
using VSHelpDesk.Infrastructure.Email;
using VSHelpDesk.Infrastructure.Storage;

namespace VSHelpDesk.Infrastructure.UnitTests.Email;

public sealed class ImapEmailReceiverIntegrationTests
{
    [GreenMailFact]
    public async Task ImapEmailReceiverIntegration_FetchMark_RemovesUnreadByReceipt()
    {
        var uniqueToken = Guid.NewGuid().ToString("N");
        var subject = $"GreenMail smoke {uniqueToken}";
        var body = $"Integration body {uniqueToken}";

        await SendSmtpAsync(subject, body);

        var options = Options.Create(new EmailOptions
        {
            ReceiverMode = "Imap",
            ImapHost = "localhost",
            ImapPort = 3143,
            ImapSecurityMode = MailTransportSecurityMode.None,
            ImapUsername = "support@vshelpdesk.test",
            ImapPassword = "test",
            ImapAccountId = "greenmail-support",
            ImapFolder = "INBOX",
            SmtpHost = "localhost",
            SmtpPort = 3025,
            SmtpSecurityMode = MailTransportSecurityMode.None,
            SupportMailboxAddress = "support@vshelpdesk.test",
            SupportMailboxDisplayName = "VS Help Desk"
        });

        await using var mailboxClient = new MailKitImapMailboxClient(
            options,
            NullLogger<MailKitImapMailboxClient>.Instance);
        var fileStorageOptions = Options.Create(new FileStorageOptions());
        var receiver = new ImapEmailReceiver(
            options,
            fileStorageOptions,
            mailboxClient,
            new HtmlToPlainTextConverter(),
            NullLogger<ImapEmailReceiver>.Instance);

        var unread = await WaitForUnreadAsync(receiver, subject);
        var match = Assert.Single(unread, m => m.Subject == subject);

        Assert.Equal(EmailReceiptKind.Imap, match.ReceiptHandle.Kind);
        Assert.StartsWith("imap\0greenmail-support\0INBOX\0", match.ReceiptHandle.Value, StringComparison.Ordinal);
        Assert.Equal("customer@vshelpdesk.test", match.FromAddress);
        Assert.Equal(body, match.Body);
        Assert.False(match.IsHtml);

        await receiver.MarkAsProcessedAsync(match.ReceiptHandle);

        var afterMark = await receiver.FetchUnreadAsync();
        Assert.DoesNotContain(
            afterMark,
            m => m.ReceiptHandle.Value == match.ReceiptHandle.Value);
    }

    private static async Task SendSmtpAsync(string subject, string body)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress("Customer", "customer@vshelpdesk.test"));
        message.To.Add(new MailboxAddress("Support", "support@vshelpdesk.test"));
        message.Subject = subject;
        message.Body = new TextPart("plain") { Text = body };

        using var client = new SmtpClient();
        await client.ConnectAsync("localhost", 3025, SecureSocketOptions.None);
        await client.SendAsync(message);
        await client.DisconnectAsync(true);
    }

    private static async Task<IReadOnlyList<IncomingEmail>> WaitForUnreadAsync(
        IEmailReceiver receiver,
        string subject)
    {
        const int maxAttempts = 20;
        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            var unread = await receiver.FetchUnreadAsync();
            if (unread.Any(m => m.Subject == subject))
            {
                return unread;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250));
        }

        throw new TimeoutException(
            $"GreenMail did not expose unread message with subject token within timeout.");
    }
}
