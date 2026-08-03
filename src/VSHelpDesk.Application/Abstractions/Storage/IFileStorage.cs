namespace VSHelpDesk.Application.Abstractions.Storage;

/// <summary>
/// File storage outside web root (BR-012, BR-017) — Hafta 3.
/// </summary>
public interface IFileStorage
{
    Task<StoredFile> SaveAsync(
        Stream content,
        string originalFileName,
        string contentType,
        CancellationToken cancellationToken = default);

    Task<Stream> OpenReadAsync(string storedFileName, CancellationToken cancellationToken = default);

    Task DeleteAsync(string storedFileName, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> ListStoredFilesAsync(CancellationToken cancellationToken = default);
}

public sealed record StoredFile(
    string StoredFileName,
    string FilePath,
    string ContentType,
    long FileSize);
