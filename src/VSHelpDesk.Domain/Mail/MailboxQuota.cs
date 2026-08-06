namespace VSHelpDesk.Domain.Mail;

/// <summary>Single source for mailbox quotas (blocker 6). Used by both Application and Infrastructure.</summary>
public static class MailboxQuota
{
    public const int MaxMessagesPerRun = 100;
    public const int MaxAttachmentsPerMessage = 10;
    public const long MaxAggregateBytesPerRun = 50 * 1024 * 1024;
    public const long MaxRawMessageBytes = 5 * 1024 * 1024;
}
