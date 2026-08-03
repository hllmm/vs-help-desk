namespace VSHelpDesk.Application.Common;

/// <summary>
/// Application-level error, exception, and validation messages (tr-TR default).
/// </summary>
public static class ApplicationMessages
{
    public static class Auth
    {
        public const string InvalidCredentials = "Geçersiz kullanıcı adı veya şifre.";
        public const string UserInactive = "Kullanıcı hesabı aktif değil.";
        public const string Unauthorized = "Yetkisiz erişim.";
    }

    public static class Tickets
    {
        public static string NotFound(object idOrNumber) => $"Ticket '{idOrNumber}' was not found.";
        public static string MessageNotFound(object messageId) => $"Ticket message '{messageId}' was not found.";
        public const string ResolvedTicketSupportReply = "A resolved ticket cannot receive a support reply.";
        public const string FromAddressMismatch = "From address does not match the ticket customer email.";
        public const string SubjectRequired = "Subject is required.";
        public const string CustomerNameRequired = "CustomerName is required.";
        public const string CustomerEmailRequired = "CustomerEmail is required.";
        public const string IdempotencyKeyRequired = "IdempotencyKey is required.";
        public const string TicketNumberRequired = "TicketNumber is required.";
        public const string ContentRequired = "Content is required.";
        public const string ConcurrentUpdate = "Could not update ticket due to a concurrent update.";
    }

    public static class Attachments
    {
        public const string FileNameRequired = "File name is required.";
        public const string FileContentRequired = "File content is required.";
        public static string MaxSizeBytesExceeded(long maxBytes) => $"File exceeds the maximum allowed size of {maxBytes} bytes.";
        public static string ContentTypeNotAllowed(string contentType) => $"Content type '{contentType}' is not allowed.";
        public static string MessageNotFound(object messageId) => $"Ticket message '{messageId}' was not found.";
        public const string FailedToReadFile = "Failed to read the uploaded file.";
        public const string ContentTypeMismatch = "File content does not match the declared content type.";
        public const string FailedToStoreFile = "Failed to store the uploaded file.";
        public const string FailedToPersistMetadata = "Failed to persist attachment metadata.";
        public static string NotFound(object attachmentId) => $"Attachment '{attachmentId}' was not found.";
    }

    public static class Users
    {
        public static string EmailAlreadyRegistered(string email) => $"Email '{email}' is already registered.";
        public static string NotFound(object userId) => $"User '{userId}' was not found.";
        public const string CannotDeactivateLastAdmin = "Cannot deactivate or demote the last Active Admin user.";
        public const string UsernameRequired = "Username is required.";
        public const string PasswordRequired = "Password is required.";
    }

    public static class Parameters
    {
        public static string NotFound(string key) => $"Parameter '{key}' was not found.";
        public const string InvalidValue = "Parameter value is invalid.";
        public const string NameRequired = "Parameter name is required.";
    }

    public static class MailProcessing
    {
        public static string JobAlreadyRunning(string jobName) => $"The '{jobName}' job is already running.";
        public const string FailedToProcessIncomingEmail = "Failed to process incoming email.";
    }

    public static class Common
    {
        public static string NotFoundTemplate(string entityName, object key) => $"{entityName} '{key}' was not found.";
        public const string OptimisticConcurrencyConflict = "The entity was modified by another operation.";
    }
}
