using VSHelpDesk.Domain.Entities;
using VSHelpDesk.Domain.Enums;

namespace VSHelpDesk.Application.Features.ScheduledJobs.ResolveInactiveTickets;

/// <summary>Fixed inclusive three-day inactivity threshold (BR-008). Not configuration-bound.</summary>
public static class ResolveInactiveTicketsPolicy
{
    public static readonly TimeSpan InactivityThreshold = TimeSpan.FromDays(3);

    public static bool IsEligible(Ticket ticket, DateTime cutoffUtc) =>
        ticket.Status == TicketStatus.WaitingCustomerReply
        && ticket.WaitingCustomerSince is DateTime since
        && since <= cutoffUtc;
}
