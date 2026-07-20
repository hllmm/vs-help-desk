namespace VSHelpDesk.Domain.Enums;

public enum ProcessedEmailDisposition
{
    LegacyProcessed = 1,
    CreatedTicket = 2,
    AppendedReply = 3,
    Quarantined = 4
}
