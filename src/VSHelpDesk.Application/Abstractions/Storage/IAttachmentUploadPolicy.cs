namespace VSHelpDesk.Application.Abstractions.Storage;

/// <summary>MIME and size limits for portal uploads (mentor-configurable defaults).</summary>
public interface IAttachmentUploadPolicy
{
    long MaxFileSizeBytes { get; }

    bool IsContentTypeAllowed(string? contentType);

    bool IsFileNameValid(string? fileName) => !string.IsNullOrWhiteSpace(fileName);

    bool IsExtensionConsistentWithContentType(string? fileName, string? declaredContentType) => true;

    /// <summary>
    /// Sniff leading file bytes and return a canonical MIME when recognized;
    /// returns null when the signature is unknown (caller may fall back to declared type only if allowed).
    /// </summary>
    string? DetectContentTypeFromContent(ReadOnlySpan<byte> header);

    /// <summary>
    /// True when declared MIME is allowed and either matches the sniff or the sniff is unknown
    /// for types that cannot be reliably detected (e.g. plain text).
    /// </summary>
    bool IsDeclaredTypeConsistentWithContent(string? declaredContentType, ReadOnlySpan<byte> header);

    bool IsDeclaredTypeConsistentWithContent(string? fileName, string? declaredContentType, ReadOnlySpan<byte> header) =>
        IsDeclaredTypeConsistentWithContent(declaredContentType, header);

    /// <summary>
    /// Stream-aware overload that allows full ZIP central-directory scan for macro detection.
    /// Default falls back to header-only check for backward compatibility.
    /// </summary>
    bool IsDeclaredTypeConsistentWithContent(string? fileName, string? declaredContentType, Stream content, ReadOnlySpan<byte> header) =>
        IsDeclaredTypeConsistentWithContent(fileName, declaredContentType, header);
}
