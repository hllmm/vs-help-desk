namespace VSHelpDesk.Application.Common.Exceptions;

public sealed class ResolvedTicketReplyException(string message = "Çözümlenmiş bir Tickete destek yanıtı gönderilemez.")
    : ConflictApplicationException(message);
