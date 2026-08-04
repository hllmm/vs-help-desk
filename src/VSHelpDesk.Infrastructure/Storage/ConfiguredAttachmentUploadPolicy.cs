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

        if (header.Length >= 6 &&
            header[0] == 0x47 && header[1] == 0x49 && header[2] == 0x46 && header[3] == 0x38)
        {
            return "image/gif";
        }

        if (header.Length >= 12 &&
            header[0] == 0x52 && header[1] == 0x49 && header[2] == 0x46 && header[3] == 0x46 &&
            header[8] == 0x57 && header[9] == 0x45 && header[10] == 0x42 && header[11] == 0x50)
        {
            return "image/webp";
        }

        // Zip archive / OpenXML (docx, xlsx)
        if (header.Length >= 4 &&
            header[0] == 0x50 && header[1] == 0x4B && header[2] == 0x03 && header[3] == 0x04)
        {
            return "application/zip";
        }

        // Legacy OLE Compound File Binary Format (.xls)
        if (header.Length >= 4 &&
            header[0] == 0xD0 && header[1] == 0xCF && header[2] == 0x11 && header[3] == 0xE0)
        {
            return "application/vnd.ms-excel";
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

        // Reject macro-enabled Office formats and legacy msword
        if (declared.Contains("macroEnabled", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(declared, "application/msword", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // Dangerous sniffed types are always rejected even if not in allow-list path above.
        if (detected is "application/x-msdownload")
        {
            return false;
        }

        // When we can detect a strong signature, require an exact match to the declaration.
        if (detected is not null)
        {
            if (detected is "application/zip" &&
                declared.StartsWith("application/vnd.openxmlformats-officedocument.", StringComparison.OrdinalIgnoreCase))
            {
                if (ContainsBytesIgnoreCase(header, "vbaProject.bin"u8))
                {
                    return false;
                }

                return true;
            }

            return string.Equals(detected, declared, StringComparison.OrdinalIgnoreCase);
        }

        // Unknown binary signatures for media types that should have signatures → reject.
        if (declared.StartsWith("image/", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(declared, "application/pdf", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(declared, "application/vnd.ms-excel", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // text/plain: no magic signature, validate text encoding and absence of binary/null control bytes.
        if (string.Equals(declared, "text/plain", StringComparison.OrdinalIgnoreCase))
        {
            return IsValidTextContent(header);
        }

        return true;
    }

    private static bool IsValidTextContent(ReadOnlySpan<byte> content)
    {
        if (content.IsEmpty)
        {
            return true;
        }

        if (!System.Text.Unicode.Utf8.IsValid(content))
        {
            return false;
        }

        foreach (var b in content)
        {
            if (b == 0x00)
            {
                return false;
            }

            if (b < 0x08 || (b > 0x0D && b < 0x20 && b != 0x1B) || b == 0x7F)
            {
                return false;
            }
        }

        return true;
    }

    private static bool ContainsBytesIgnoreCase(ReadOnlySpan<byte> haystack, ReadOnlySpan<byte> needle)
    {
        if (needle.IsEmpty || haystack.Length < needle.Length)
        {
            return false;
        }

        for (var i = 0; i <= haystack.Length - needle.Length; i++)
        {
            var match = true;
            for (var j = 0; j < needle.Length; j++)
            {
                var h = (char)haystack[i + j];
                var n = (char)needle[j];
                if (char.ToLowerInvariant(h) != char.ToLowerInvariant(n))
                {
                    match = false;
                    break;
                }
            }

            if (match)
            {
                return true;
            }
        }

        return false;
    }
}
