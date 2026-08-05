namespace VSHelpDesk.Infrastructure.Email;

public sealed class MailboxQuotaOptions
{
    public const string SectionName = "MailboxQuota";

    public int MaxMessagesPerRun { get; init; } = 100;

    public int MaxAttachmentsPerMessage { get; init; } = 10;

    public long MaxAggregateBytesPerRun { get; init; } = 50 * 1024 * 1024;

    public long MaxRawMessageBytes { get; init; } = 5 * 1024 * 1024;
}
