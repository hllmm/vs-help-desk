using Microsoft.Extensions.Options;
using VSHelpDesk.Application.Abstractions.Storage;

namespace VSHelpDesk.Infrastructure.Storage;

public sealed class ConfiguredAttachmentUploadPolicy(IOptions<FileStorageOptions> options)
    : IAttachmentUploadPolicy
{
    private readonly HashSet<string> allowed = new(
        options.Value.AllowedContentTypes ?? [],
        StringComparer.OrdinalIgnoreCase);

    public long MaxFileSizeBytes => options.Value.MaxFileSizeBytes;

    public bool IsContentTypeAllowed(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
        {
            return false;
        }

        // Strip optional charset/parameters: "text/plain; charset=utf-8"
        var mime = contentType.Split(';', 2)[0].Trim();
        return allowed.Contains(mime);
    }

    public string? DetectContentTypeFromContent(ReadOnlySpan<byte> header)
    {
        if (header.Length >= 8 &&
            header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47)
        {
            return "image/png";
        }

        if (header.Length >= 3 &&
            header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF)
        {
            return "image/jpeg";
        }

        if (header.Length >= 4 &&
            header[0] == 0x25 && header[1] == 0x50 && header[2] == 0x44 && header[3] == 0x46)
        {
            return "application/pdf";
        }

        // PE executable / DLL
        if (header.Length >= 2 && header[0] == 0x4D && header[1] == 0x5A)
        {
            return "application/x-msdownload";
        }

        return null;
    }

    public bool IsDeclaredTypeConsistentWithContent(string? declaredContentType, ReadOnlySpan<byte> header)
    {
        if (!IsContentTypeAllowed(declaredContentType))
        {
            return false;
        }

        var declared = declaredContentType!.Split(';', 2)[0].Trim();
        var detected = DetectContentTypeFromContent(header);

        // Dangerous sniffed types are always rejected even if not in allow-list path above.
        if (detected is "application/x-msdownload")
        {
            return false;
        }

        // When we can detect a strong signature, require an exact match to the declaration.
        if (detected is not null)
        {
            return string.Equals(detected, declared, StringComparison.OrdinalIgnoreCase);
        }

        // Unknown binary signatures for media types that should have signatures → reject.
        if (declared.StartsWith("image/", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(declared, "application/pdf", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // text/plain and similar: no reliable magic; allow when content type is allowed.
        return true;
    }
}
