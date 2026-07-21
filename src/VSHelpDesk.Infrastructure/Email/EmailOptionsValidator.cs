using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using MimeKit;

namespace VSHelpDesk.Infrastructure.Email;

public sealed class EmailOptionsValidator(IHostEnvironment environment)
    : IValidateOptions<EmailOptions>
{
    public ValidateOptionsResult Validate(string? name, EmailOptions options)
    {
        var mode = (options.ReceiverMode ?? string.Empty).Trim();
        if (!mode.Equals("Fake", StringComparison.OrdinalIgnoreCase) &&
            !mode.Equals("Imap", StringComparison.OrdinalIgnoreCase))
        {
            return ValidateOptionsResult.Fail(
                "The Email:ReceiverMode configuration value must be 'Fake' or 'Imap'.");
        }

        var isLocal =
            environment.IsDevelopment() ||
            environment.IsEnvironment("Testing");

        if (mode.Equals("Fake", StringComparison.OrdinalIgnoreCase) && !isLocal)
        {
            return ValidateOptionsResult.Fail(
                "Email:ReceiverMode 'Fake' is not allowed outside Development/Testing. Use Imap with real credentials.");
        }

        if (!isLocal &&
            (options.SmtpSecurityMode == MailTransportSecurityMode.None ||
             options.ImapSecurityMode == MailTransportSecurityMode.None))
        {
            return ValidateOptionsResult.Fail(
                "Email transport security mode 'None' is not allowed outside Development/Testing. Use StartTls or SslOnConnect.");
        }

        if (string.IsNullOrWhiteSpace(options.SmtpHost))
        {
            return ValidateOptionsResult.Fail("The Email:SmtpHost configuration value is required.");
        }

        if (options.SmtpPort <= 0)
        {
            return ValidateOptionsResult.Fail("The Email:SmtpPort configuration value must be positive.");
        }

        if (!IsDefined(options.SmtpSecurityMode))
        {
            return ValidateOptionsResult.Fail(
                "The Email:SmtpSecurityMode configuration value must be None, StartTls, or SslOnConnect.");
        }

        if (!IsDefined(options.ImapSecurityMode))
        {
            return ValidateOptionsResult.Fail(
                "The Email:ImapSecurityMode configuration value must be None, StartTls, or SslOnConnect.");
        }

        if (string.IsNullOrWhiteSpace(options.SupportMailboxAddress))
        {
            return ValidateOptionsResult.Fail(
                "The Email:SupportMailboxAddress configuration value is required.");
        }

        if (ContainsControlCharacters(options.SupportMailboxAddress))
        {
            return ValidateOptionsResult.Fail(
                "The Email:SupportMailboxAddress configuration value must not contain control characters.");
        }

        var supportAddress = options.SupportMailboxAddress.Trim();
        if (!MailboxAddress.TryParse(supportAddress, out var parsedSupport) ||
            !string.Equals(parsedSupport.Address, supportAddress, StringComparison.OrdinalIgnoreCase))
        {
            return ValidateOptionsResult.Fail(
                "The Email:SupportMailboxAddress configuration value must be a valid mailbox address.");
        }

        if (!string.IsNullOrEmpty(options.SupportMailboxDisplayName) &&
            ContainsControlCharacters(options.SupportMailboxDisplayName))
        {
            return ValidateOptionsResult.Fail(
                "The Email:SupportMailboxDisplayName configuration value must not contain control characters.");
        }

        if (mode.Equals("Imap", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(options.ImapHost))
            {
                return ValidateOptionsResult.Fail(
                    "The Email:ImapHost configuration value is required when ReceiverMode is Imap.");
            }

            if (options.ImapPort <= 0)
            {
                return ValidateOptionsResult.Fail(
                    "The Email:ImapPort configuration value must be positive when ReceiverMode is Imap.");
            }

            if (string.IsNullOrWhiteSpace(options.ImapUsername) ||
                string.IsNullOrWhiteSpace(options.ImapPassword))
            {
                return ValidateOptionsResult.Fail(
                    "Email:ImapUsername and Email:ImapPassword are required when ReceiverMode is Imap (use user-secrets).");
            }

            if (string.IsNullOrWhiteSpace(options.ImapAccountId))
            {
                return ValidateOptionsResult.Fail(
                    "The Email:ImapAccountId configuration value is required when ReceiverMode is Imap.");
            }

            if (ContainsControlCharacters(options.ImapAccountId))
            {
                return ValidateOptionsResult.Fail(
                    "The Email:ImapAccountId configuration value must not contain control characters.");
            }

            if (string.IsNullOrWhiteSpace(options.ImapFolder))
            {
                return ValidateOptionsResult.Fail(
                    "The Email:ImapFolder configuration value is required when ReceiverMode is Imap.");
            }

            if (ContainsControlCharacters(options.ImapFolder))
            {
                return ValidateOptionsResult.Fail(
                    "The Email:ImapFolder configuration value must not contain control characters.");
            }
        }

        return ValidateOptionsResult.Success;
    }

    private static bool IsDefined(MailTransportSecurityMode mode) =>
        mode is MailTransportSecurityMode.None
            or MailTransportSecurityMode.StartTls
            or MailTransportSecurityMode.SslOnConnect;

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
