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

    public string[] AllowedContentTypes { get; init; } =
    [
        "application/pdf",
        "image/png",
        "image/jpeg",
        "image/gif",
        "image/webp",
        "text/plain"
    ];
}
