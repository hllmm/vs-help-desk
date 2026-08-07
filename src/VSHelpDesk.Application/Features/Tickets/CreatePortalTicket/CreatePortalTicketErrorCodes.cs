namespace VSHelpDesk.Application.Features.Tickets.CreatePortalTicket;

public static class CreatePortalTicketErrorCodes
{
    public const string PayloadConflict = "portal-idempotency-payload-conflict";
    public const string InvalidPayload = "portal-ticket-payload-invalid";
    public const string UserRequired = "portal-authenticated-user-required";
}
