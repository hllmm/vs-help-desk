namespace VSHelpDesk.WebAPI.Contracts.Tickets;

public sealed record CreateTicketRequest(string Subject, string CustomerName, string CustomerEmail, string Content);
