namespace VSHelpDesk.Domain.Entities;

public sealed class TicketAttachment
{
    public Guid Id { get; private set; } = Guid.NewGuid();

    public Guid TicketMessageId { get; private set; }

    public string FileName { get; private set; } = string.Empty;

    public string StoredFileName { get; private set; } = string.Empty;

    public string FilePath { get; private set; } = string.Empty;

    public string ContentType { get; private set; } = string.Empty;

    public long FileSize { get; private set; }

    public DateTime CreatedAt { get; private set; } = DateTime.UtcNow;

    private TicketAttachment()
    {
    }

    public TicketAttachment(
        Guid ticketMessageId,
        string fileName,
        string storedFileName,
        string filePath,
        string contentType,
        long fileSize)
    {
        TicketMessageId = ticketMessageId;
        FileName = fileName;
        StoredFileName = storedFileName;
        FilePath = filePath;
        ContentType = contentType;
        FileSize = fileSize;
    }
}
