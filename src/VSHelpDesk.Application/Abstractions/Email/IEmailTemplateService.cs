namespace VSHelpDesk.Application.Abstractions.Email;

/// <summary>
/// Service for wrapping outgoing notification contents in corporate HTML and plain-text templates.
/// </summary>
public interface IEmailTemplateService
{
    string WrapInCorporateTemplate(
        string title,
        string body,
        string? actionUrl = null,
        string? actionText = null);

    string GeneratePlainTextAlternative(
        string title,
        string body,
        string? actionUrl = null,
        string? actionText = null);
}
