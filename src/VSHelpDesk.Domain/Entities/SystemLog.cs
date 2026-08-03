namespace VSHelpDesk.Domain.Entities;

public sealed class SystemLog
{
    public Guid Id { get; private set; } = Guid.NewGuid();

    public string LogLevel { get; private set; } = string.Empty;

    public string Message { get; private set; } = string.Empty;

    public string? Exception { get; private set; }

    public string CategoryName { get; private set; } = string.Empty;

    public int? EventId { get; private set; }

    public DateTime CreatedAt { get; private set; } = DateTime.UtcNow;

    private SystemLog()
    {
    }

    public SystemLog(
        string logLevel,
        string message,
        string? exception = null,
        string? categoryName = null,
        int? eventId = null,
        DateTime? createdAt = null)
    {
        Id = Guid.NewGuid();
        LogLevel = logLevel;
        Message = message;
        Exception = exception;
        CategoryName = categoryName ?? string.Empty;
        EventId = eventId;
        CreatedAt = createdAt ?? DateTime.UtcNow;
    }
}
