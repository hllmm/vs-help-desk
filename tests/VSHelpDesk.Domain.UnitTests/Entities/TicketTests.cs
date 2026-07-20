using System.Reflection;
using VSHelpDesk.Domain.Entities;
using VSHelpDesk.Domain.Enums;
using VSHelpDesk.Domain.Tickets;

namespace VSHelpDesk.Domain.UnitTests.Entities;

public sealed class TicketTests
{
    private static readonly DateTime T0 = new(2026, 7, 28, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime T1 = T0.AddMinutes(10);
    private static readonly DateTime T2 = T0.AddMinutes(20);
    private static readonly DateTime T3 = T0.AddMinutes(30);
    private static readonly DateTime T4 = T0.AddMinutes(40);

    [Fact]
    public void Create_SetsNewStatusSubjectAndTimestamps_WithoutMutatingSubjectLater()
    {
        var ticket = Ticket.Create("VS-000001", "Printer offline", "Ada", "ada@example.test", T0);

        Assert.Equal(TicketStatus.New, ticket.Status);
        Assert.Equal("VS-000001", ticket.TicketNumber);
        Assert.Equal("Printer offline", ticket.Subject);
        Assert.Equal(T0, ticket.CreatedAt);
        Assert.Equal(T0, ticket.UpdatedAt);
        Assert.Equal(T0, ticket.LastActivityAt);

        // BR-021: no public setter for Subject (reply/reopen cannot change it).
        var subjectSetter = typeof(Ticket).GetProperty(nameof(Ticket.Subject))!.SetMethod;
        Assert.NotNull(subjectSetter);
        Assert.True(subjectSetter.IsPrivate);
    }

    [Fact]
    public void BR019_RecordMessageActivity_UpdatesUpdatedAtAndLastActivityAtOnly()
    {
        var ticket = Ticket.Create("VS-000002", "Subject", "Ada", "ada@example.test", T0);
        var originalSubject = ticket.Subject;

        ticket.RecordMessageActivity(T1);

        Assert.Equal(T0, ticket.CreatedAt);
        Assert.Equal(T1, ticket.UpdatedAt);
        Assert.Equal(T1, ticket.LastActivityAt);
        Assert.Equal(originalSubject, ticket.Subject);
        Assert.Equal(TicketStatus.New, ticket.Status);
    }

    [Fact]
    public void StatusCycle_New_Waiting_CustomerReplied_Waiting()
    {
        var ticket = Ticket.Create("VS-000003", "Cycle", "Ada", "ada@example.test", T0);

        ticket.MarkAsWaitingCustomerReply(T1);
        Assert.Equal(TicketStatus.WaitingCustomerReply, ticket.Status);
        Assert.Equal(T1, ticket.WaitingCustomerSince);
        Assert.Equal(T1, ticket.LastActivityAt);

        ticket.MarkAsCustomerReplied(T2);
        Assert.Equal(TicketStatus.CustomerReplied, ticket.Status);
        Assert.Null(ticket.WaitingCustomerSince);
        Assert.Equal(T2, ticket.LastActivityAt);

        ticket.MarkAsWaitingCustomerReply(T3);
        Assert.Equal(TicketStatus.WaitingCustomerReply, ticket.Status);
        Assert.Equal(T3, ticket.WaitingCustomerSince);

        // Additional support reply while already waiting (UC-005 follow-up).
        ticket.MarkAsWaitingCustomerReply(T3.AddMinutes(1));
        Assert.Equal(TicketStatus.WaitingCustomerReply, ticket.Status);
        Assert.Equal(T3.AddMinutes(1), ticket.WaitingCustomerSince);
    }

    [Fact]
    public void BR010_Resolved_ThenCustomerReply_ReopensToCustomerReplied()
    {
        var closer = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var ticket = Ticket.Create("VS-000004", "Reopen", "Ada", "ada@example.test", T0);

        ticket.Resolve(T1, closer);
        Assert.Equal(TicketStatus.Resolved, ticket.Status);
        Assert.Equal(T1, ticket.ResolvedAt);
        Assert.Equal(closer, ticket.ClosedByUserId);

        ticket.MarkAsCustomerReplied(T2);
        Assert.Equal(TicketStatus.CustomerReplied, ticket.Status);
        Assert.Null(ticket.ResolvedAt);
        Assert.Null(ticket.ClosedByUserId);
        Assert.Equal(T2, ticket.LastActivityAt);
        Assert.Equal("Reopen", ticket.Subject);
    }

    [Fact]
    public void Resolve_SetsResolvedStatusAndClearsWaiting()
    {
        var ticket = Ticket.Create("VS-000005", "Resolve", "Ada", "ada@example.test", T0);
        ticket.MarkAsWaitingCustomerReply(T1);

        ticket.Resolve(T2);

        Assert.Equal(TicketStatus.Resolved, ticket.Status);
        Assert.Equal(T2, ticket.ResolvedAt);
        Assert.Null(ticket.WaitingCustomerSince);
        Assert.Equal(T2, ticket.LastActivityAt);
    }

    [Fact]
    public void Assign_UpdatesAssigneeAndUpdatedAt_ButNotLastActivityAt()
    {
        var agent = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var ticket = Ticket.Create("VS-000006", "Assign", "Ada", "ada@example.test", T0);

        ticket.Assign(agent, T4);

        Assert.Equal(agent, ticket.AssignedUserId);
        Assert.Equal(T4, ticket.UpdatedAt);
        Assert.Equal(T0, ticket.LastActivityAt);
    }

    [Fact]
    public void TicketNumberFormat_FormatsAndValidatesCanonicalVsNumbers()
    {
        Assert.Equal("VS-000001", TicketNumberFormat.Format(1));
        Assert.Equal("VS-000042", TicketNumberFormat.Format(42));
        Assert.True(TicketNumberFormat.IsCanonical("VS-000001"));
        Assert.False(TicketNumberFormat.IsCanonical("vs-000001"));
        Assert.False(TicketNumberFormat.IsCanonical("VS-1"));
        Assert.Throws<ArgumentOutOfRangeException>(() => TicketNumberFormat.Format(0));
        Assert.Equal("VS-999999", TicketNumberFormat.Format(999_999));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => TicketNumberFormat.Format(1_000_000));
    }

    [Fact]
    public void IllegalTransitions_ThrowDomainException()
    {
        var ticket = Ticket.Create("VS-000020", "Guards", "Ada", "ada@example.test", T0);
        ticket.Resolve(T1);

        Assert.Throws<VSHelpDesk.Domain.Exceptions.DomainException>(
            () => ticket.Resolve(T2));
        Assert.Throws<VSHelpDesk.Domain.Exceptions.DomainException>(
            () => ticket.MarkAsWaitingCustomerReply(T2));
        Assert.Throws<VSHelpDesk.Domain.Exceptions.DomainException>(
            () => ticket.Assign(Guid.NewGuid(), T2));

        ticket.MarkAsCustomerReplied(T2);
        // CustomerReplied → CustomerReplied is allowed (additional customer messages).
        ticket.MarkAsCustomerReplied(T3);
        Assert.Equal(TicketStatus.CustomerReplied, ticket.Status);
        Assert.Equal(T3, ticket.LastActivityAt);
    }
}
