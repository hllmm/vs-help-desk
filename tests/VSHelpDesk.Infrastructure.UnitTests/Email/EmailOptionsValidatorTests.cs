using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using VSHelpDesk.Infrastructure.Email;

namespace VSHelpDesk.Infrastructure.UnitTests.Email;

public sealed class EmailOptionsValidatorTests
{
    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("Preview")]
    public void FakeMode_IsRejectedOutsideDevelopmentAndTesting(string environment)
    {
        var result = CreateValidator(environment).Validate(Options.DefaultName, ValidLocalFakeOptions());

        Assert.True(result.Failed);
        Assert.Contains("Fake", result.FailureMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Testing")]
    public void FakeMode_IsAllowedInExplicitLocalEnvironments(string environment)
    {
        var result = CreateValidator(environment).Validate(Options.DefaultName, ValidLocalFakeOptions());

        Assert.False(result.Failed);
    }

    [Fact]
    public void ProductionLikeEnvironment_RejectsNoneTransportSecurity()
    {
        var options = ValidImapOptions(smtpSecurity: MailTransportSecurityMode.None);

        var result = CreateValidator("Production").Validate(Options.DefaultName, options);

        Assert.True(result.Failed);
        Assert.Contains("None", result.FailureMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ImapMode_RequiresHostCredentialsAccountAndFolder()
    {
        var options = new EmailOptions
        {
            ReceiverMode = "Imap",
            SmtpHost = "smtp.example.test",
            SmtpPort = 587,
            SmtpSecurityMode = MailTransportSecurityMode.StartTls,
            SupportMailboxAddress = "support@vshelpdesk.local",
            SupportMailboxDisplayName = "VS Help Desk"
        };

        var result = CreateValidator("Production").Validate(Options.DefaultName, options);

        Assert.True(result.Failed);
        Assert.True(
            result.FailureMessage.Contains("ImapHost", StringComparison.OrdinalIgnoreCase)
            || result.FailureMessage.Contains("ImapUsername", StringComparison.OrdinalIgnoreCase)
            || result.FailureMessage.Contains("ImapPassword", StringComparison.OrdinalIgnoreCase)
            || result.FailureMessage.Contains("ImapAccountId", StringComparison.OrdinalIgnoreCase)
            || result.FailureMessage.Contains("ImapFolder", StringComparison.OrdinalIgnoreCase)
            || result.FailureMessage.Contains("Imap", StringComparison.OrdinalIgnoreCase));
    }

    private static EmailOptionsValidator CreateValidator(string environmentName) =>
        new(new FixedHostEnvironment { EnvironmentName = environmentName });

    private static EmailOptions ValidLocalFakeOptions() => new()
    {
        ReceiverMode = "Fake",
        SmtpHost = "localhost",
        SmtpPort = 1025,
        SmtpSecurityMode = MailTransportSecurityMode.None,
        SupportMailboxAddress = "support@vshelpdesk.local",
        SupportMailboxDisplayName = "VS Help Desk"
    };

    private static EmailOptions ValidImapOptions(
        MailTransportSecurityMode smtpSecurity = MailTransportSecurityMode.StartTls) => new()
    {
        ReceiverMode = "Imap",
        SmtpHost = "smtp.example.test",
        SmtpPort = 587,
        SmtpSecurityMode = smtpSecurity,
        SmtpUsername = "smtp-user",
        SmtpPassword = "smtp-pass",
        ImapHost = "imap.example.test",
        ImapPort = 993,
        ImapSecurityMode = MailTransportSecurityMode.SslOnConnect,
        ImapUsername = "imap-user",
        ImapPassword = "imap-pass",
        ImapAccountId = "account-1",
        ImapFolder = "INBOX",
        SupportMailboxAddress = "support@vshelpdesk.local",
        SupportMailboxDisplayName = "VS Help Desk"
    };

    private sealed class FixedHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = "tests";
        public string ContentRootPath { get; set; } = Path.GetTempPath();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
