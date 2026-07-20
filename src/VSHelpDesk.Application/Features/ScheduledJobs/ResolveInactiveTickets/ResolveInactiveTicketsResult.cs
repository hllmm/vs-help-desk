namespace VSHelpDesk.Application.Features.ScheduledJobs.ResolveInactiveTickets;

/// <summary>Safe aggregate job summary — no ticket IDs, customer data, or exception text.</summary>
public sealed record ResolveInactiveTicketsResult(
    DateTime CutoffUtc,
    int Candidates,
    int Resolved,
    int Skipped,
    int Conflicted,
    int Failed);
