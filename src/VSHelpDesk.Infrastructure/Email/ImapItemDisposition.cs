namespace VSHelpDesk.Infrastructure.Email;

public enum ImapItemDisposition
{
    Ready = 0,
    RawMessageTooLarge = 1,
    AggregateBudgetExceeded = 2,
    SizeUnavailable = 3
}
