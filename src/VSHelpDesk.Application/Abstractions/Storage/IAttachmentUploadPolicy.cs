namespace VSHelpDesk.Application.Abstractions.Storage;

/// <summary>MIME and size limits for portal uploads (mentor-configurable defaults).</summary>
public interface IAttachmentUploadPolicy
{
    long MaxFileSizeBytes { get; }

    bool IsContentTypeAllowed(string? contentType);
}
