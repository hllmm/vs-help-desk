using VSHelpDesk.Domain.Enums;
using VSHelpDesk.Domain.Exceptions;

namespace VSHelpDesk.Domain.Entities;

public sealed class Ticket
{
    public Guid Id { get; private set; } = Guid.NewGuid();

    public string TicketNumber { get; private set; } = string.Empty;

    public string Subject { get; private set; } = string.Empty;

    public string CustomerName { get; private set; } = string.Empty;

    public string CustomerEmail { get; private set; } = string.Empty;

    public TicketStatus Status { get; private set; } = TicketStatus.New;

    public Guid? AssignedUserId { get; private set; }

    public DateTime? WaitingCustomerSince { get; private set; }

    public DateTime CreatedAt { get; private set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; private set; } = DateTime.UtcNow;

    public DateTime? ResolvedAt { get; private set; }

    public DateTime LastActivityAt { get; private set; } = DateTime.UtcNow;

    public Guid? ClosedByUserId { get; private set; }

    /// <summary>PostgreSQL xmin concurrency token (Npgsql row version).</summary>
    public uint Version { get; private set; }

    /// <summary>EF Core materialization.</summary>
    private Ticket()
    {
    }

    private Ticket(
        string ticketNumber,
        string subject,
        string customerName,
        string customerEmail)
    {
        TicketNumber = ticketNumber;
        Subject = subject;
        CustomerName = customerName;
        CustomerEmail = customerEmail;
    }

    /// <summary>
    /// Creates a new ticket (UC-002). Subject is set only here (BR-021).
    /// </summary>
    public static Ticket Create(
        string ticketNumber,
        string subject,
        string customerName,
        string customerEmail,
        DateTime createdAtUtc)
    {
        return new Ticket(ticketNumber, subject, customerName, customerEmail)
        {
            Status = TicketStatus.New,
            CreatedAt = createdAtUtc,
            UpdatedAt = createdAtUtc,
            LastActivityAt = createdAtUtc
        };
    }

    /// <summary>BR-019 — bump activity when a conversation message is added.</summary>
    public void RecordMessageActivity(DateTime nowUtc)
    {
        UpdatedAt = nowUtc;
        LastActivityAt = nowUtc;
    }

    /// <summary>BR-006 — support replied; waiting on customer.</summary>
    public void MarkAsWaitingCustomerReply(DateTime now)
    {
        EnsureCanTransitionTo(
            TicketStatus.WaitingCustomerReply,
            TicketStatus.New,
            TicketStatus.CustomerReplied,
            TicketStatus.WaitingCustomerReply);

        Status = TicketStatus.WaitingCustomerReply;
        WaitingCustomerSince = now;
        UpdatedAt = now;
        LastActivityAt = now;
    }

    /// <summary>
    /// BR-007 / BR-010 — customer replied (also reopens from Resolved).
    /// Allowed from New, WaitingCustomerReply, CustomerReplied (extra message), Resolved (reopen).
    /// </summary>
    public void MarkAsCustomerReplied(DateTime now)
    {
        EnsureCanTransitionTo(
            TicketStatus.CustomerReplied,
            TicketStatus.New,
            TicketStatus.WaitingCustomerReply,
            TicketStatus.CustomerReplied,
            TicketStatus.Resolved);

        Status = TicketStatus.CustomerReplied;
        WaitingCustomerSince = null;
        ResolvedAt = null;
        ClosedByUserId = null;
        UpdatedAt = now;
        LastActivityAt = now;
    }

    /// <summary>BR-008 / BR-009 — manual or automatic resolution.</summary>
    public void Resolve(DateTime now, Guid? closedByUserId = null)
    {
        EnsureCanTransitionTo(
            TicketStatus.Resolved,
            TicketStatus.New,
            TicketStatus.WaitingCustomerReply,
            TicketStatus.CustomerReplied);

        Status = TicketStatus.Resolved;
        ResolvedAt = now;
        ClosedByUserId = closedByUserId;
        WaitingCustomerSince = null;
        UpdatedAt = now;
        LastActivityAt = now;
    }

    /// <summary>BR-011 — at most one assignee at a time. Not conversation activity (BR-019).</summary>
    public void Assign(Guid userId, DateTime now)
    {
        if (userId == Guid.Empty)
        {
            throw new DomainException("Assignee user id is required.");
        }

        if (Status == TicketStatus.Resolved)
        {
            throw new DomainException(
                $"Cannot assign a ticket in status '{Status}'.");
        }

        AssignedUserId = userId;
        UpdatedAt = now;
    }

    private void EnsureCanTransitionTo(TicketStatus target, params TicketStatus[] allowedFrom)
    {
        if (Array.IndexOf(allowedFrom, Status) < 0)
        {
            throw new DomainException(
                $"Cannot transition ticket from '{Status}' to '{target}'.");
        }
    }
}
