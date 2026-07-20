using Microsoft.Extensions.Options;

namespace VSHelpDesk.Infrastructure.Storage;

public sealed class FileStorageOptionsValidator : IValidateOptions<FileStorageOptions>
{
    public ValidateOptionsResult Validate(string? name, FileStorageOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.RootPath))
        {
            return ValidateOptionsResult.Fail(
                "The FileStorage:RootPath configuration value is required.");
        }

        if (options.MaxFileSizeBytes <= 0)
        {
            return ValidateOptionsResult.Fail(
                "The FileStorage:MaxFileSizeBytes configuration value must be positive.");
        }

        if (options.AllowedContentTypes is null || options.AllowedContentTypes.Length == 0)
        {
            return ValidateOptionsResult.Fail(
                "The FileStorage:AllowedContentTypes configuration value must list at least one MIME type.");
        }

        var normalizedRoot = options.RootPath.Replace('\\', '/').TrimEnd('/');
        if (normalizedRoot.Contains("wwwroot", StringComparison.OrdinalIgnoreCase))
        {
            return ValidateOptionsResult.Fail(
                "FileStorage:RootPath must not point at wwwroot (BR-017).");
        }

        return ValidateOptionsResult.Success;
    }
}
