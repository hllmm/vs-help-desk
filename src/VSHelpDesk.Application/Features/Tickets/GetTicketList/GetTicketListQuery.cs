using VSHelpDesk.Domain.Enums;

namespace VSHelpDesk.Application.Features.Tickets.GetTicketList;

/// <summary>UC-003 — optional status filter; default sort LastActivityAt desc.</summary>
public sealed record GetTicketListQuery(TicketStatus? Status = null);
