namespace VSHelpDesk.Infrastructure.Logging;

/// <summary>
/// Sanitizes sensitive PII, secrets, and credentials from log messages and exception details.
/// </summary>
public interface ILogPropertySanitizer
{
    string? Sanitize(string? text);
}
