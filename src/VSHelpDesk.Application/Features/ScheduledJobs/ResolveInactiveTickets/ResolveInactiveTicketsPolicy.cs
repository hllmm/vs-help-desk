using VSHelpDesk.Domain.Entities;
using VSHelpDesk.Domain.Enums;

namespace VSHelpDesk.Application.Features.ScheduledJobs.ResolveInactiveTickets;

/// <summary>
/// Eligibility rules for auto-resolve (BR-008). Threshold days come from
/// <c>AutoResolve.InactiveDays</c> via <see cref="Abstractions.Parameters.IApplicationParameterReader"/>;
/// <see cref="DefaultInactivityDays"/> is the fail-closed default.
/// </summary>
public static class ResolveInactiveTicketsPolicy
{
    /// <summary>Default inactivity days when the parameter is missing, corrupt, or invalid (&lt; 1).</summary>
    public const int DefaultInactivityDays = 3;

    public static bool IsEligible(Ticket ticket, DateTime cutoffUtc) =>
        ticket.Status == TicketStatus.WaitingCustomerReply
        && ticket.WaitingCustomerSince is DateTime since
        && since <= cutoffUtc;
}
