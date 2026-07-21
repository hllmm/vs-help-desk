namespace VSHelpDesk.Domain.Enums;

/// <summary>BR-018 — the complete supported ticket status set.</summary>
public enum TicketStatus
{
    New = 1,
    WaitingCustomerReply = 2,
    CustomerReplied = 3,
    Resolved = 4
}
