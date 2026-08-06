namespace VSHelpDesk.Application.Abstractions.Email;

/// <summary>Single runtime quota source (blocker 5). Bound from Infrastructure's MailboxQuotaOptions.</summary>
public interface IMailboxQuotaSettings
{
    int MaxMessagesPerRun { get; }
    int MaxAttachmentsPerMessage { get; }
    long MaxAggregateBytesPerRun { get; }
    long MaxRawMessageBytes { get; }
}
