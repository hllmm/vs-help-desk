namespace VSHelpDesk.Domain.Entities;

/// <summary>File metadata for a ticket message (BR-012). Bytes live outside the DB (BR-017).</summary>
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

    /// <summary>EF Core materialization.</summary>
    private TicketAttachment()
    {
    }

    public TicketAttachment(
        Guid ticketMessageId,
        string fileName,
        string storedFileName,
        string filePath,
        string contentType,
        long fileSize,
        DateTime? createdAtUtc = null)
    {
        TicketMessageId = ticketMessageId;
        FileName = fileName;
        StoredFileName = storedFileName;
        FilePath = filePath;
        ContentType = contentType;
        FileSize = fileSize;
        CreatedAt = createdAtUtc ?? DateTime.UtcNow;
    }
}
