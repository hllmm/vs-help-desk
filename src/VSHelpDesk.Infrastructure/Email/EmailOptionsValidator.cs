using Microsoft.Extensions.Options;

namespace VSHelpDesk.Infrastructure.Email;

public sealed class EmailOptionsValidator : IValidateOptions<EmailOptions>
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

        // Fake samples must never process production mailboxes.
        if (mode.Equals("Fake", StringComparison.OrdinalIgnoreCase) && IsProductionLikeEnvironment())
        {
            return ValidateOptionsResult.Fail(
                "Email:ReceiverMode 'Fake' is not allowed in Production/Staging. Use Imap with real credentials.");
        }

        if (string.IsNullOrWhiteSpace(options.SmtpHost))
        {
            return ValidateOptionsResult.Fail("The Email:SmtpHost configuration value is required.");
        }

        if (options.SmtpPort <= 0)
        {
            return ValidateOptionsResult.Fail("The Email:SmtpPort configuration value must be positive.");
        }

        if (string.IsNullOrWhiteSpace(options.SupportMailboxAddress))
        {
            return ValidateOptionsResult.Fail(
                "The Email:SupportMailboxAddress configuration value is required.");
        }

        if (mode.Equals("Imap", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(options.ImapHost))
            {
                return ValidateOptionsResult.Fail(
                    "The Email:ImapHost configuration value is required when ReceiverMode is Imap.");
            }

            if (string.IsNullOrWhiteSpace(options.Username) || string.IsNullOrWhiteSpace(options.Password))
            {
                return ValidateOptionsResult.Fail(
                    "Email:Username and Email:Password are required when ReceiverMode is Imap (use user-secrets).");
            }
        }

        return ValidateOptionsResult.Success;
    }

    private static bool IsProductionLikeEnvironment()
    {
        var environment =
            Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
            ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
            ?? string.Empty;

        return environment.Equals("Production", StringComparison.OrdinalIgnoreCase)
            || environment.Equals("Staging", StringComparison.OrdinalIgnoreCase);
    }
}
