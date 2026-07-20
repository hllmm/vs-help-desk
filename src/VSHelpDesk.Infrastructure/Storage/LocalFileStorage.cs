using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VSHelpDesk.Application.Abstractions.Storage;

namespace VSHelpDesk.Infrastructure.Storage;

/// <summary>Disk storage under a configurable root outside wwwroot (BR-012, BR-017).</summary>
public sealed class LocalFileStorage : IFileStorage
{
    private readonly string absoluteRoot;
    private readonly ILogger<LocalFileStorage> logger;

    public LocalFileStorage(
        IOptions<FileStorageOptions> options,
        IHostEnvironment hostEnvironment,
        ILogger<LocalFileStorage> logger)
    {
        this.logger = logger;
        var configured = options.Value.RootPath.Trim();
        absoluteRoot = Path.IsPathRooted(configured)
            ? Path.GetFullPath(configured)
            : Path.GetFullPath(Path.Combine(hostEnvironment.ContentRootPath, configured));

        Directory.CreateDirectory(absoluteRoot);

        logger.LogInformation(
            "Local file storage root resolved to {StorageRoot}",
            absoluteRoot);
    }

    public string AbsoluteRoot => absoluteRoot;

    public async Task<StoredFile> SaveAsync(
        Stream content,
        string originalFileName,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        var extension = Path.GetExtension(originalFileName);
        if (extension.Length > 16)
        {
            extension = string.Empty;
        }

        var storedFileName = $"{Guid.NewGuid():N}{extension}";
        var absolutePath = Path.Combine(absoluteRoot, storedFileName);

        await using (var fileStream = new FileStream(
            absolutePath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 81920,
            options: FileOptions.Asynchronous | FileOptions.SequentialScan))
        {
            await content.CopyToAsync(fileStream, cancellationToken);
        }

        var fileInfo = new FileInfo(absolutePath);
        logger.LogInformation(
            "Stored attachment file storedFileName={StoredFileName} size={FileSize} contentType={ContentType}",
            storedFileName,
            fileInfo.Length,
            contentType);

        return new StoredFile(
            StoredFileName: storedFileName,
            FilePath: absolutePath,
            ContentType: contentType,
            FileSize: fileInfo.Length);
    }

    public Task<Stream> OpenReadAsync(string storedFileName, CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;
        var absolutePath = ResolveExistingPath(storedFileName);
        Stream stream = new FileStream(
            absolutePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 81920,
            options: FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Task.FromResult(stream);
    }

    public Task DeleteAsync(string storedFileName, CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;
        var absolutePath = Path.Combine(absoluteRoot, Path.GetFileName(storedFileName));
        if (File.Exists(absolutePath))
        {
            File.Delete(absolutePath);
            logger.LogInformation("Deleted stored file storedFileName={StoredFileName}", storedFileName);
        }

        return Task.CompletedTask;
    }

    private string ResolveExistingPath(string storedFileName)
    {
        var safeName = Path.GetFileName(storedFileName);
        if (string.IsNullOrWhiteSpace(safeName) ||
            !string.Equals(safeName, storedFileName, StringComparison.Ordinal))
        {
            throw new FileNotFoundException("Stored file name is invalid.", storedFileName);
        }

        var absolutePath = Path.Combine(absoluteRoot, safeName);
        if (!File.Exists(absolutePath))
        {
            throw new FileNotFoundException("Stored attachment file was not found.", safeName);
        }

        return absolutePath;
    }
}
