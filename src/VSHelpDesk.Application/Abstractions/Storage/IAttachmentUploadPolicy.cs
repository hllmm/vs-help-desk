namespace VSHelpDesk.Application.Abstractions.Storage;

/// <summary>MIME and size limits for portal uploads (mentor-configurable defaults).</summary>
public interface IAttachmentUploadPolicy
{
    long MaxFileSizeBytes { get; }

    bool IsContentTypeAllowed(string? contentType);

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
}
