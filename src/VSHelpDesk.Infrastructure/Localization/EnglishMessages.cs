
using VSHelpDesk.Application.Common.Localization;

namespace VSHelpDesk.Infrastructure.Localization;

/// <summary>English (en-US) message dictionary.</summary>
internal static class EnglishMessages
{
    internal static readonly IReadOnlyDictionary<string, string> Messages =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [MessageKeys.Auth.InvalidCredentials] = "Invalid username or password.",
            [MessageKeys.Auth.UserInactive] = "The user account is inactive.",
            [MessageKeys.Auth.Unauthorized] = "Unauthorized access.",
            [MessageKeys.Tickets.NotFound] = "Ticket '{0}' could not be found.",
            [MessageKeys.Tickets.MessageNotFound] = "Ticket message '{0}' could not be found.",
            [MessageKeys.Tickets.ResolvedTicketSupportReply] = "A support reply cannot be sent to a resolved ticket.",
            [MessageKeys.Tickets.FromAddressMismatch] = "The sender address does not match the ticket customer email.",
            [MessageKeys.Tickets.SubjectRequired] = "Ticket subject is required.",
            [MessageKeys.Tickets.CustomerNameRequired] = "Customer name is required.",
            [MessageKeys.Tickets.CustomerEmailRequired] = "Customer email is required.",
            [MessageKeys.Tickets.IdempotencyKeyRequired] = "Idempotency key is required.",
            [MessageKeys.Tickets.TicketNumberRequired] = "Ticket number is required.",
            [MessageKeys.Tickets.ContentRequired] = "Content is required.",
            [MessageKeys.Tickets.ConcurrentUpdate] = "The ticket could not be updated because of a concurrent change.",
            [MessageKeys.Attachments.FileRequired] = "A file is required.",
            [MessageKeys.Attachments.FileNameRequired] = "File name is required.",
            [MessageKeys.Attachments.FileContentRequired] = "File content is required.",
            [MessageKeys.Attachments.MaxSizeBytesExceeded] = "The file exceeds the maximum allowed size of {0} bytes.",
            [MessageKeys.Attachments.ContentTypeNotAllowed] = "Content type '{0}' is not allowed.",
            [MessageKeys.Attachments.MessageNotFound] = "Ticket message '{0}' could not be found.",
            [MessageKeys.Attachments.FailedToReadFile] = "The uploaded file could not be read.",
            [MessageKeys.Attachments.ContentTypeMismatch] = "The file content does not match the declared content type.",
            [MessageKeys.Attachments.FailedToStoreFile] = "The uploaded file could not be stored.",
            [MessageKeys.Attachments.FailedToPersistMetadata] = "Attachment metadata could not be saved.",
            [MessageKeys.Attachments.NotFound] = "Attachment '{0}' could not be found.",
            [MessageKeys.Attachments.StorageFileNotFound] = "The storage file for attachment '{0}' could not be found.",
            [MessageKeys.Users.EmailAlreadyRegistered] = "Email address '{0}' is already registered.",
            [MessageKeys.Users.NotFound] = "User '{0}' could not be found.",
            [MessageKeys.Users.CannotDeactivateLastAdmin] = "The last active administrator cannot be deactivated or demoted.",
            [MessageKeys.Users.UsernameRequired] = "Username is required.",
            [MessageKeys.Users.PasswordRequired] = "Password is required.",
            [MessageKeys.Parameters.NotFound] = "Parameter '{0}' could not be found.",
            [MessageKeys.Parameters.InvalidValue] = "The parameter value is invalid.",
            [MessageKeys.Parameters.NameRequired] = "Parameter name is required.",
            [MessageKeys.MailProcessing.JobAlreadyRunning] = "Job '{0}' is already running.",
            [MessageKeys.MailProcessing.FailedToProcessIncomingEmail] = "The incoming email could not be processed.",
            [MessageKeys.MailProcessing.FailedToFetchUnreadEmails] = "Unread emails could not be fetched from the configured receiver.",
            [MessageKeys.Email.AcknowledgementSubject] = "[{0}] We received your support request",
            [MessageKeys.Email.AcknowledgementBody] = "Hello,{1}{1}We received your message and opened ticket {0}.{1}Please keep {0} in the subject when you reply.{1}{1}VS Help Desk",
            [MessageKeys.Common.NotFoundTemplate] = "{0} record '{1}' could not be found.",
            [MessageKeys.Common.OptimisticConcurrencyConflict] = "The entity was changed by another operation.",
            [MessageKeys.Http.RateLimitExceeded] = "Too many sign-in attempts. Please try again later.",
            [MessageKeys.Http.BadRequest] = "The request is invalid.",
            [MessageKeys.Http.NotFound] = "The requested resource could not be found.",
            [MessageKeys.Http.Unauthorized] = "Unauthorized access.",
            [MessageKeys.Http.Conflict] = "The request conflicts with the current state.",
            [MessageKeys.Http.DomainRuleViolation] = "A domain rule was violated.",
            [MessageKeys.Http.UnexpectedError] = "An unexpected error occurred."
        };
}
