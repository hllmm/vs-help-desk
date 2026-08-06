using System.IO.Compression;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using VSHelpDesk.Application.Abstractions.Storage;

namespace VSHelpDesk.Infrastructure.Storage;

public sealed class ConfiguredAttachmentUploadPolicy(IOptions<FileStorageOptions> options)
    : IAttachmentUploadPolicy
{
    private readonly HashSet<string> allowed = new(
        options.Value.AllowedContentTypes ?? [],
        StringComparer.OrdinalIgnoreCase);

    // Extension → MIME allowlist (SEC-006). Only modern OpenXML plus legacy xls; msword explicitly excluded.
    private static readonly Dictionary<string, string> ExtensionMap = new(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = "application/pdf",
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".gif"] = "image/gif",
        [".webp"] = "image/webp",
        [".txt"] = "text/plain",
        [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        [".xls"] = "application/vnd.ms-excel",
    };

    // Filename charset: letters, digits, dot, underscore, hyphen, space. Extension length already capped at 16 chars.
    private static readonly Regex FileNameRegex = new(@"^[a-zA-Z0-9._\- ]+$", RegexOptions.Compiled);

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

    public bool IsFileNameValid(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return false;
        }

        var safe = Path.GetFileName(fileName.Trim());
        if (string.IsNullOrWhiteSpace(safe))
        {
            return false;
        }

        // Path.GetFileName must be idempotent — rejects directory traversal
        if (!string.Equals(safe, fileName.Trim(), StringComparison.Ordinal))
        {
            // Allow original with path stripped but still validate resulting name
            // Check contained traversal chars already stripped; if original had '/', GetFileName differs → treat as traversal attempt if original contained separator
            if (fileName.Contains('/') || fileName.Contains('\\'))
            {
                return false;
            }
        }

        if (safe.Length is < 1 or > 255)
        {
            return false;
        }

        var ext = Path.GetExtension(safe);
        if (ext.Length > 16)
        {
            return false;
        }

        // Validate charset without extension? Full name including extension must match
        // Remove extension for regex or keep? Brief says charset a-zA-Z0-9._- (and space). Use full name.
        // Replace to ensure regex covers name without path.
        if (!FileNameRegex.IsMatch(safe))
        {
            return false;
        }

        return true;
    }

    public bool IsExtensionConsistentWithContentType(string? fileName, string? declaredContentType)
    {
        if (string.IsNullOrWhiteSpace(fileName) || string.IsNullOrWhiteSpace(declaredContentType))
        {
            return false;
        }

        var ext = Path.GetExtension(fileName.Trim());
        if (string.IsNullOrWhiteSpace(ext))
        {
            return false;
        }

        if (!ExtensionMap.TryGetValue(ext, out var expectedMime))
        {
            return false;
        }

        var declared = declaredContentType.Split(';', 2)[0].Trim();
        return string.Equals(expectedMime, declared, StringComparison.OrdinalIgnoreCase);
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
        => IsDeclaredTypeConsistentWithContent(null, declaredContentType, header);

    public bool IsDeclaredTypeConsistentWithContent(string? fileName, string? declaredContentType, Stream content, ReadOnlySpan<byte> header)
        => IsDeclaredTypeConsistentWithContent(fileName, declaredContentType, content, header, CancellationToken.None);

    public bool IsDeclaredTypeConsistentWithContent(string? fileName, string? declaredContentType, Stream content, ReadOnlySpan<byte> header, CancellationToken cancellationToken = default)
    {
        if (!IsContentTypeAllowed(declaredContentType))
        {
            return false;
        }

        var declared = declaredContentType!.Split(';', 2)[0].Trim();
        var detected = DetectContentTypeFromContent(header);

        if (declared.Contains("macroEnabled", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(declared, "application/msword", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(fileName))
        {
            if (!IsExtensionConsistentWithContentType(fileName, declared))
            {
                return false;
            }

            if (!IsFileNameValid(fileName))
            {
                return false;
            }
        }

        if (detected is "application/x-msdownload")
        {
            return false;
        }

        if (detected is not null)
        {
            if (detected is "application/zip" &&
                declared.StartsWith("application/vnd.openxmlformats-officedocument.", StringComparison.OrdinalIgnoreCase))
            {
                if (!IsSafeOoxmlArchive(content, cancellationToken)) return false;
                return true;
            }

            return string.Equals(detected, declared, StringComparison.OrdinalIgnoreCase);
        }

        if (declared.StartsWith("image/", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(declared, "application/pdf", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(declared, "application/vnd.ms-excel", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (string.Equals(declared, "text/plain", StringComparison.OrdinalIgnoreCase))
        {
            return IsValidTextContent(header);
        }

        return true;
    }

    public bool IsDeclaredTypeConsistentWithContent(string? fileName, string? declaredContentType, ReadOnlySpan<byte> header)
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

        // Extension allowlist enforcement when filename is provided
        if (!string.IsNullOrWhiteSpace(fileName))
        {
            // Validate extension maps to declared mime; reject unknown extensions
            if (!IsExtensionConsistentWithContentType(fileName, declared))
            {
                return false;
            }

            if (!IsFileNameValid(fileName))
            {
                return false;
            }
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
                // Header-only fallback: scan header slice for macro marker without full archive.
                // Full fail-closed scan requires stream overload with IsSafeOoxmlArchive.
                if (System.Text.Encoding.UTF8.GetString(header).Contains("vbaProject.bin", StringComparison.OrdinalIgnoreCase))
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

    private bool IsSafeOoxmlArchive(Stream content, CancellationToken ct)
    {
        if (content is null) return false;
        try{
            using var archive = new ZipArchive(content, ZipArchiveMode.Read, leaveOpen:true);
            int count=0;
            foreach(var e in archive.Entries){
                ct.ThrowIfCancellationRequested();
                if(++count > 4096) return false;
                if(e.FullName.Contains("vbaProject.bin", StringComparison.OrdinalIgnoreCase)) return false;
            }
            return true;
        }catch(InvalidDataException){ return false; }
        catch(OperationCanceledException){ throw; }
        catch{ return false; }
        finally{ if(content.CanSeek) content.Position=0; }
    }
}
