using Microsoft.Extensions.Options;
using System.Text;
using VSHelpDesk.Application.Abstractions.Storage;

namespace VSHelpDesk.Infrastructure.Storage;

public sealed class ConfiguredAttachmentUploadPolicy(IOptions<FileStorageOptions> options)
    : IAttachmentUploadPolicy
{
    private static readonly IReadOnlyDictionary<string, string> ContentTypesByExtension =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [".png"] = "image/png",
            [".jpg"] = "image/jpeg",
            [".jpeg"] = "image/jpeg",
            [".gif"] = "image/gif",
            [".webp"] = "image/webp",
            [".pdf"] = "application/pdf",
            [".txt"] = "text/plain"
        };

    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    private readonly HashSet<string> allowed = new(
        options.Value.AllowedContentTypes ?? [],
        StringComparer.OrdinalIgnoreCase);

    public long MaxFileSizeBytes => options.Value.MaxFileSizeBytes;

    public AttachmentValidationResult Validate(
        string fileName,
        string? declaredContentType,
        ReadOnlySpan<byte> content)
    {
        if (content.IsEmpty)
        {
            return AttachmentValidationResult.Rejected("File content is required.");
        }

        if (content.Length >= 2 && content[0] == 0x4D && content[1] == 0x5A)
        {
            return AttachmentValidationResult.Rejected("Executable file content is not allowed.");
        }

        if (string.IsNullOrWhiteSpace(fileName))
        {
            return AttachmentValidationResult.Rejected("File name is required.");
        }

        var extension = Path.GetExtension(fileName.Trim());
        if (!ContentTypesByExtension.TryGetValue(extension, out var expectedContentType))
        {
            return AttachmentValidationResult.Rejected("File extension is not allowed.");
        }

        if (string.IsNullOrWhiteSpace(declaredContentType))
        {
            return AttachmentValidationResult.Rejected("Content type is required.");
        }

        var declared = declaredContentType.Split(';', 2)[0].Trim();
        if (!allowed.Contains(expectedContentType) ||
            !string.Equals(declared, expectedContentType, StringComparison.OrdinalIgnoreCase))
        {
            return AttachmentValidationResult.Rejected(
                "File extension and declared content type are not allowed or do not match.");
        }

        var contentMatches = expectedContentType switch
        {
            "image/png" => HasPrefix(
                content,
                [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]),
            "image/jpeg" =>
                HasPrefix(content, [0xFF, 0xD8, 0xFF]) &&
                HasSuffix(content, [0xFF, 0xD9]),
            "image/gif" =>
                HasPrefix(content, "GIF87a"u8) ||
                HasPrefix(content, "GIF89a"u8),
            "image/webp" =>
                content.Length >= 12 &&
                content[..4].SequenceEqual("RIFF"u8) &&
                content.Slice(8, 4).SequenceEqual("WEBP"u8),
            "application/pdf" => IsPdf(content),
            "text/plain" => IsValidPlainText(content),
            _ => false
        };

        return contentMatches
            ? AttachmentValidationResult.Allowed(expectedContentType)
            : AttachmentValidationResult.Rejected(
                "File content does not match its extension and declared content type.");
    }

    private static bool HasPrefix(ReadOnlySpan<byte> content, ReadOnlySpan<byte> prefix) =>
        content.StartsWith(prefix);

    private static bool HasSuffix(ReadOnlySpan<byte> content, ReadOnlySpan<byte> suffix) =>
        content.EndsWith(suffix);

    private static bool IsPdf(ReadOnlySpan<byte> content)
    {
        if (!HasPrefix(content, "%PDF-"u8))
        {
            return false;
        }

        var trailerLength = Math.Min(content.Length, 1024);
        return content[^trailerLength..].IndexOf("%%EOF"u8) >= 0;
    }

    private static bool IsValidPlainText(ReadOnlySpan<byte> content)
    {
        try
        {
            var text = StrictUtf8.GetString(content);
            return text.All(character =>
                !char.IsControl(character) ||
                character is '\t' or '\r' or '\n');
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }
}
