namespace VSHelpDesk.Application.Common.Localization;

/// <summary>
/// Strongly-typed message key constants for <see cref="IMessageProvider"/>.
/// Keys follow a dot-separated convention matching the static class hierarchy.
/// </summary>
public static class MessageKeys
{
    public static class Auth
    {
        public const string InvalidCredentials = "Auth.InvalidCredentials";
        public const string UserInactive = "Auth.UserInactive";
        public const string Unauthorized = "Auth.Unauthorized";
    }

    public static class Tickets
    {
        public const string NotFound = "Tickets.NotFound";
        public const string MessageNotFound = "Tickets.MessageNotFound";
        public const string ResolvedTicketSupportReply = "Tickets.ResolvedTicketSupportReply";
        public const string FromAddressMismatch = "Tickets.FromAddressMismatch";
        public const string SubjectRequired = "Tickets.SubjectRequired";
        public const string CustomerNameRequired = "Tickets.CustomerNameRequired";
        public const string CustomerEmailRequired = "Tickets.CustomerEmailRequired";
        public const string IdempotencyKeyRequired = "Tickets.IdempotencyKeyRequired";
        public const string TicketNumberRequired = "Tickets.TicketNumberRequired";
        public const string ContentRequired = "Tickets.ContentRequired";
        public const string ConcurrentUpdate = "Tickets.ConcurrentUpdate";
    }

    public static class Attachments
    {
        public const string FileRequired = "Attachments.FileRequired";
        public const string FileNameRequired = "Attachments.FileNameRequired";
        public const string FileContentRequired = "Attachments.FileContentRequired";
        public const string MaxSizeBytesExceeded = "Attachments.MaxSizeBytesExceeded";
        public const string ContentTypeNotAllowed = "Attachments.ContentTypeNotAllowed";
        public const string MessageNotFound = "Attachments.MessageNotFound";
        public const string FailedToReadFile = "Attachments.FailedToReadFile";
        public const string ContentTypeMismatch = "Attachments.ContentTypeMismatch";
        public const string FailedToStoreFile = "Attachments.FailedToStoreFile";
        public const string FailedToPersistMetadata = "Attachments.FailedToPersistMetadata";
        public const string NotFound = "Attachments.NotFound";
        public const string StorageFileNotFound = "Attachments.StorageFileNotFound";
    }

    public static class Users
    {
        public const string EmailAlreadyRegistered = "Users.EmailAlreadyRegistered";
        public const string NotFound = "Users.NotFound";
        public const string CannotDeactivateLastAdmin = "Users.CannotDeactivateLastAdmin";
        public const string UsernameRequired = "Users.UsernameRequired";
        public const string PasswordRequired = "Users.PasswordRequired";
    }

    public static class Parameters
    {
        public const string NotFound = "Parameters.NotFound";
        public const string InvalidValue = "Parameters.InvalidValue";
        public const string NameRequired = "Parameters.NameRequired";
    }

    public static class MailProcessing
    {
        public const string JobAlreadyRunning = "MailProcessing.JobAlreadyRunning";
        public const string FailedToProcessIncomingEmail = "MailProcessing.FailedToProcessIncomingEmail";
        public const string FailedToFetchUnreadEmails = "MailProcessing.FailedToFetchUnreadEmails";
    }

    public static class Email
    {
        public const string AcknowledgementSubject = "Email.AcknowledgementSubject";
        public const string AcknowledgementBody = "Email.AcknowledgementBody";
    }
    public static class Common
    {
        public const string NotFoundTemplate = "Common.NotFoundTemplate";
        public const string OptimisticConcurrencyConflict = "Common.OptimisticConcurrencyConflict";
    }

    public static class Http
    {
        public const string RateLimitExceeded = "Http.RateLimitExceeded";
        public const string BadRequest = "Http.BadRequest";
        public const string NotFound = "Http.NotFound";
        public const string Unauthorized = "Http.Unauthorized";
        public const string Conflict = "Http.Conflict";
        public const string DomainRuleViolation = "Http.DomainRuleViolation";
        public const string UnexpectedError = "Http.UnexpectedError";
    }
}
