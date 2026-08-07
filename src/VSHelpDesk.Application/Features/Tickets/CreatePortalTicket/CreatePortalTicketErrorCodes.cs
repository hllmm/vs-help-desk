namespace VSHelpDesk.Application.Features.Tickets.CreatePortalTicket;

public static class CreatePortalTicketErrorCodes
{
    public const string PayloadConflict = "portal-idempotency-payload-conflict";
    public const string InvalidPayload = "portal-ticket-payload-invalid";
    public const string UserRequired = "portal-authenticated-user-required";
    public const string IdempotencyKeyRequired = "portal-idempotency-key-required";
    public const string IdempotencyKeyInvalid = "portal-idempotency-key-invalid";
    public const string SubjectRequired = "portal-ticket-subject-required";
    public const string CustomerNameRequired = "portal-ticket-customer-name-required";
    public const string CustomerEmailRequired = "portal-ticket-customer-email-required";
    public const string CustomerEmailInvalid = "portal-ticket-customer-email-invalid";
    public const string ContentRequired = "portal-ticket-content-required";
    public const string ContentTooLong = "portal-ticket-content-too-long";
}
