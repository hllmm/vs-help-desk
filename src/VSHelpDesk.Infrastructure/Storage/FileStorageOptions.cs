namespace VSHelpDesk.Infrastructure.Storage;

public sealed class FileStorageOptions
{
    public const string SectionName = "FileStorage";

    /// <summary>
    /// Storage root relative to content root (e.g. <c>storage</c>) or absolute path.
    /// Must stay outside wwwroot (BR-017).
    /// </summary>
    public string RootPath { get; init; } = "storage";

    /// <summary>Default 10 MiB — mentor-approved baseline for internship.</summary>
    public long MaxFileSizeBytes { get; init; } = 10 * 1024 * 1024;

    /// <summary>How often the orphan cleanup pass runs.</summary>
    public int OrphanCleanupPeriodMinutes { get; init; } = 30;

    /// <summary>
    /// Minimum file age before a storage-only file may be deleted. This prevents
    /// cleanup from racing a file upload whose database transaction has not committed yet.
    /// </summary>
    public int OrphanGracePeriodMinutes { get; init; } = 60;

    public string[] AllowedContentTypes { get; init; } =
    [
        "application/pdf",
        "image/png",
        "image/jpeg",
        "image/gif",
        "image/webp",
        "text/plain",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        "application/vnd.ms-excel",
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
    ];
}
