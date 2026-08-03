namespace VSHelpDesk.Application.Abstractions.Email;

public interface IEmailTemplateService
{
    string WrapInCorporateTemplate(
        string title,
        string body,
        string? actionUrl = null,
        string? actionText = null);
}
