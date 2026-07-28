namespace VSHelpDesk.Application.Abstractions.Storage;

public sealed record AttachmentValidationResult(
    bool IsAllowed,
    string? CanonicalContentType,
    string? Error)
{
    public static AttachmentValidationResult Allowed(string canonicalContentType) =>
        new(true, canonicalContentType, null);

    public static AttachmentValidationResult Rejected(string error) =>
        new(false, null, error);
}
