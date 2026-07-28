namespace VSHelpDesk.Application.Abstractions.Storage;

/// <summary>Complete file-content, MIME, extension, and size policy shared by attachment writers.</summary>
public interface IAttachmentUploadPolicy
{
    long MaxFileSizeBytes { get; }

    AttachmentValidationResult Validate(
        string fileName,
        string? declaredContentType,
        ReadOnlySpan<byte> content);
}
