using VSHelpDesk.Domain.Mail;

namespace VSHelpDesk.Infrastructure.Email;

public sealed class MailboxQuotaOptions
{
    public const string SectionName = "MailboxQuota";

    public int MaxMessagesPerRun { get; init; } = MailboxQuota.MaxMessagesPerRun;

    public int MaxAttachmentsPerMessage { get; init; } = MailboxQuota.MaxAttachmentsPerMessage;

    public long MaxAggregateBytesPerRun { get; init; } = MailboxQuota.MaxAggregateBytesPerRun;

    public long MaxRawMessageBytes { get; init; } = MailboxQuota.MaxRawMessageBytes;
}
