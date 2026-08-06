using Microsoft.Extensions.Options;

namespace VSHelpDesk.Infrastructure.Email;

public sealed class MailboxQuotaOptionsValidator : IValidateOptions<MailboxQuotaOptions>
{
    public ValidateOptionsResult Validate(string? name, MailboxQuotaOptions options)
    {
        if (options.MaxMessagesPerRun <= 0)
        {
            return ValidateOptionsResult.Fail($"{nameof(MailboxQuotaOptions.MaxMessagesPerRun)} must be > 0 (was {options.MaxMessagesPerRun}).");
        }

        if (options.MaxAttachmentsPerMessage <= 0)
        {
            return ValidateOptionsResult.Fail($"{nameof(MailboxQuotaOptions.MaxAttachmentsPerMessage)} must be > 0 (was {options.MaxAttachmentsPerMessage}).");
        }

        if (options.MaxAggregateBytesPerRun <= 0)
        {
            return ValidateOptionsResult.Fail($"{nameof(MailboxQuotaOptions.MaxAggregateBytesPerRun)} must be > 0 (was {options.MaxAggregateBytesPerRun}).");
        }

        if (options.MaxRawMessageBytes <= 0)
        {
            return ValidateOptionsResult.Fail($"{nameof(MailboxQuotaOptions.MaxRawMessageBytes)} must be > 0 (was {options.MaxRawMessageBytes}).");
        }

        return ValidateOptionsResult.Success;
    }
}
