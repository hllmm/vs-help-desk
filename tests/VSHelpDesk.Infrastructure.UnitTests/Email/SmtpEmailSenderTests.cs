using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using VSHelpDesk.Application.Abstractions.Email;
using VSHelpDesk.Infrastructure.Email;

namespace VSHelpDesk.Infrastructure.UnitTests.Email;

public sealed class SmtpEmailSenderTests
{
    [Fact]
    public async Task SmtpSender_CrLfRecipient_IsRejectedBeforeNetworkConnect()
    {
        var sender = new SmtpEmailSender(
            Options.Create(new EmailOptions
            {
                SmtpHost = "127.0.0.1",
                SmtpPort = 1,
                SmtpSecurityMode = MailTransportSecurityMode.None,
                SupportMailboxAddress = "support@vshelpdesk.local",
                SupportMailboxDisplayName = "VS Help Desk"
            }),
            NullLogger<SmtpEmailSender>.Instance);

        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            sender.SendAsync(
                new EmailMessage(
                    "victim@example.test\r\nBcc: attacker@example.test",
                    "Victim",
                    "Subject",
                    "Body")));

        Assert.Contains("recipient", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Connection refused", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("actively refused", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}
