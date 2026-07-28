using System.Security.Cryptography;
using VSHelpDesk.Domain.Enums;
using VSHelpDesk.Domain.Exceptions;
using VSHelpDesk.Domain.Tickets;

namespace VSHelpDesk.Domain.Entities;

public sealed class Ticket
{
    public Guid Id { get; private set; } = Guid.NewGuid();

    public string TicketNumber { get; private set; } = string.Empty;

    public string ReplyToken { get; private set; } = CreateReplyToken();

    public string ReplyReference =>
        TicketReplyReference.Format(TicketNumber, ReplyToken);

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

    /// <summary>BR-009 — manual resolution by an authenticated support user.</summary>
    public bool ResolveManually(DateTime nowUtc, Guid closedByUserId)
    {
        if (closedByUserId == Guid.Empty)
        {
            throw new DomainException("Closing user id is required.");
        }

        if (Status == TicketStatus.Resolved)
        {
            return false;
        }

        EnsureCanTransitionTo(
            TicketStatus.Resolved,
            TicketStatus.New,
            TicketStatus.WaitingCustomerReply,
            TicketStatus.CustomerReplied);

        ApplyResolution(nowUtc, closedByUserId);
        return true;
    }

    /// <summary>BR-008 — automatic system resolution (no closer user).</summary>
    public bool ResolveAutomatically(DateTime nowUtc)
    {
        if (Status == TicketStatus.Resolved)
        {
            return false;
        }

        EnsureCanTransitionTo(
            TicketStatus.Resolved,
            TicketStatus.WaitingCustomerReply);

        ApplyResolution(nowUtc, closedByUserId: null);
        return true;
    }

    private void ApplyResolution(DateTime nowUtc, Guid? closedByUserId)
    {
        Status = TicketStatus.Resolved;
        ResolvedAt = nowUtc;
        ClosedByUserId = closedByUserId;
        WaitingCustomerSince = null;
        UpdatedAt = nowUtc;
        LastActivityAt = nowUtc;
    }

    /// <summary>BR-011 — at most one assignee at a time. Not conversation activity (BR-019).</summary>
    public bool Assign(Guid userId, DateTime now)
    {
        EnsureAssignmentCanChange();

        if (userId == Guid.Empty)
        {
            throw new DomainException("assignee-required");
        }

        if (AssignedUserId == userId)
        {
            return false;
        }

        AssignedUserId = userId;
        UpdatedAt = now;
        return true;
    }

    /// <summary>BR-011 — clears the single assignee without conversation activity.</summary>
    public bool Unassign(DateTime now)
    {
        EnsureAssignmentCanChange();

        if (AssignedUserId is null)
        {
            return false;
        }

        AssignedUserId = null;
        UpdatedAt = now;
        return true;
    }

    private void EnsureAssignmentCanChange()
    {
        if (Status == TicketStatus.Resolved)
        {
            throw new DomainException("ticket-resolved");
        }
    }

    private void EnsureCanTransitionTo(TicketStatus target, params TicketStatus[] allowedFrom)
    {
        if (Array.IndexOf(allowedFrom, Status) < 0)
        {
            throw new DomainException(
                $"Cannot transition ticket from '{Status}' to '{target}'.");
        }
    }

    private static string CreateReplyToken() =>
        Convert.ToHexString(RandomNumberGenerator.GetBytes(16))
            .ToLowerInvariant();
}
