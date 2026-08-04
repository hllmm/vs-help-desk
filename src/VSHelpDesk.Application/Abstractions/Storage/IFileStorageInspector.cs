namespace VSHelpDesk.Application.Abstractions.Storage;

/// <summary>
/// Provides metadata required by maintenance jobs without exposing provider-specific paths.
/// Storage implementations that support orphan cleanup should implement this interface.
/// </summary>
public interface IFileStorageInspector
{
    Task<IReadOnlyList<StoredFileEntry>> ListStoredFileEntriesAsync(
        CancellationToken cancellationToken = default);
}

public sealed record StoredFileEntry(
    string StoredFileName,
    DateTimeOffset LastModifiedAtUtc,
    long FileSize);
