using System.Reflection;
using VSHelpDesk.Domain.Entities;
using VSHelpDesk.Domain.Enums;
using VSHelpDesk.Domain.Exceptions;
using VSHelpDesk.Domain.Tickets;

namespace VSHelpDesk.Domain.UnitTests.Entities;

public sealed class TicketTests
{
    private static readonly DateTime T0 = new(2026, 7, 28, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime T1 = T0.AddMinutes(10);
    private static readonly DateTime T2 = T0.AddMinutes(20);
    private static readonly DateTime T3 = T0.AddMinutes(30);
    private static readonly DateTime T4 = T0.AddMinutes(40);
    private static readonly Guid SupportUserId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OtherUserId = Guid.Parse("22222222-2222-2222-2222-222222222222");

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

    [Theory]
    [InlineData(TicketStatus.New)]
    [InlineData(TicketStatus.WaitingCustomerReply)]
    [InlineData(TicketStatus.CustomerReplied)]
    public void ResolveManually_OpenStatus_SetsUserAndClosureFields(TicketStatus startingStatus)
    {
        var ticket = CreateInStatus(startingStatus, "VS-000004");

        var changed = ticket.ResolveManually(T1, SupportUserId);

        Assert.True(changed);
        Assert.Equal(TicketStatus.Resolved, ticket.Status);
        Assert.Equal(T1, ticket.ResolvedAt);
        Assert.Equal(SupportUserId, ticket.ClosedByUserId);
        Assert.Null(ticket.WaitingCustomerSince);
        Assert.Equal(T1, ticket.UpdatedAt);
        Assert.Equal(T1, ticket.LastActivityAt);
    }

    [Fact]
    public void ResolveManually_EmptyUserId_ThrowsWithoutMutation()
    {
        var ticket = Ticket.Create("VS-000005", "Empty closer", "Ada", "ada@example.test", T0);
        var before = CapturePublicState(ticket);

        var ex = Assert.Throws<DomainException>(() => ticket.ResolveManually(T1, Guid.Empty));

        Assert.Equal("Closing user id is required.", ex.Message);
        AssertPublicStateEqual(before, ticket);
    }

    [Fact]
    public void ResolveManually_AlreadyResolved_IsIdempotentAndPreservesOriginalClosure()
    {
        var ticket = Ticket.Create("VS-000006", "Idempotent manual", "Ada", "ada@example.test", T0);
        Assert.True(ticket.ResolveManually(T1, SupportUserId));
        var before = CapturePublicState(ticket);

        var repeated = ticket.ResolveManually(T2, OtherUserId);

        Assert.False(repeated);
        AssertPublicStateEqual(before, ticket);
        Assert.Equal(SupportUserId, ticket.ClosedByUserId);
        Assert.Equal(T1, ticket.ResolvedAt);
    }

    [Fact]
    public void ResolveAutomatically_WaitingAtAnyAge_SetsSystemClosureFields()
    {
        var ticket = Ticket.Create("VS-000007", "Auto resolve", "Ada", "ada@example.test", T0);
        ticket.MarkAsWaitingCustomerReply(T1);

        var automatic = ticket.ResolveAutomatically(T2);

        Assert.True(automatic);
        Assert.Equal(TicketStatus.Resolved, ticket.Status);
        Assert.Equal(T2, ticket.ResolvedAt);
        Assert.Null(ticket.ClosedByUserId);
        Assert.Null(ticket.WaitingCustomerSince);
        Assert.Equal(T2, ticket.UpdatedAt);
        Assert.Equal(T2, ticket.LastActivityAt);
    }

    [Theory]
    [InlineData(TicketStatus.New)]
    [InlineData(TicketStatus.CustomerReplied)]
    public void ResolveAutomatically_NonWaitingOpenStatus_ThrowsWithoutMutation(
        TicketStatus startingStatus)
    {
        var ticket = CreateInStatus(startingStatus, "VS-000008");
        var before = CapturePublicState(ticket);

        Assert.Throws<DomainException>(() => ticket.ResolveAutomatically(T1));

        AssertPublicStateEqual(before, ticket);
    }

    [Fact]
    public void ResolveAutomatically_AlreadyResolved_IsIdempotentAndPreservesOriginalClosure()
    {
        var ticket = Ticket.Create("VS-000009", "Idempotent auto", "Ada", "ada@example.test", T0);
        ticket.MarkAsWaitingCustomerReply(T1);
        Assert.True(ticket.ResolveAutomatically(T2));
        var before = CapturePublicState(ticket);

        var repeated = ticket.ResolveAutomatically(T3);

        Assert.False(repeated);
        AssertPublicStateEqual(before, ticket);
        Assert.Null(ticket.ClosedByUserId);
        Assert.Equal(T2, ticket.ResolvedAt);
    }

    [Fact]
    public void CustomerReply_AfterManualOrAutomaticResolution_ReopensAndClearsClosure()
    {
        var manual = Ticket.Create("VS-000010", "Reopen manual", "Ada", "ada@example.test", T0);
        Assert.True(manual.ResolveManually(T1, SupportUserId));

        manual.MarkAsCustomerReplied(T2);

        Assert.Equal(TicketStatus.CustomerReplied, manual.Status);
        Assert.Null(manual.ResolvedAt);
        Assert.Null(manual.ClosedByUserId);
        Assert.Equal(T2, manual.LastActivityAt);
        Assert.Equal("Reopen manual", manual.Subject);

        var automatic = Ticket.Create("VS-000011", "Reopen auto", "Ada", "ada@example.test", T0);
        automatic.MarkAsWaitingCustomerReply(T1);
        Assert.True(automatic.ResolveAutomatically(T2));

        automatic.MarkAsCustomerReplied(T3);

        Assert.Equal(TicketStatus.CustomerReplied, automatic.Status);
        Assert.Null(automatic.ResolvedAt);
        Assert.Null(automatic.ClosedByUserId);
        Assert.Equal(T3, automatic.LastActivityAt);
        Assert.Equal("Reopen auto", automatic.Subject);
    }

    [Fact]
    public void Assign_UpdatesAssigneeAndUpdatedAt_ButNotLastActivityAt()
    {
        var agent = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var ticket = Ticket.Create("VS-000012", "Assign", "Ada", "ada@example.test", T0);

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
        Assert.True(ticket.ResolveManually(T1, SupportUserId));

        Assert.Throws<DomainException>(() => ticket.MarkAsWaitingCustomerReply(T2));
        Assert.Throws<DomainException>(() => ticket.Assign(Guid.NewGuid(), T2));

        ticket.MarkAsCustomerReplied(T2);
        // CustomerReplied → CustomerReplied is allowed (additional customer messages).
        ticket.MarkAsCustomerReplied(T3);
        Assert.Equal(TicketStatus.CustomerReplied, ticket.Status);
        Assert.Equal(T3, ticket.LastActivityAt);
    }

    private static Ticket CreateInStatus(TicketStatus status, string ticketNumber)
    {
        var ticket = Ticket.Create(ticketNumber, "Subject", "Ada", "ada@example.test", T0);
        switch (status)
        {
            case TicketStatus.New:
                break;
            case TicketStatus.WaitingCustomerReply:
                ticket.MarkAsWaitingCustomerReply(T0.AddMinutes(1));
                break;
            case TicketStatus.CustomerReplied:
                ticket.MarkAsCustomerReplied(T0.AddMinutes(1));
                break;
            default:
                throw new InvalidOperationException($"Unexpected starting status {status}.");
        }

        return ticket;
    }

    private static IReadOnlyDictionary<string, object?> CapturePublicState(Ticket ticket)
    {
        return typeof(Ticket)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .ToDictionary(property => property.Name, property => property.GetValue(ticket));
    }

    private static void AssertPublicStateEqual(
        IReadOnlyDictionary<string, object?> expected,
        Ticket ticket)
    {
        var actual = CapturePublicState(ticket);
        Assert.Equal(expected.Count, actual.Count);
        foreach (var (name, value) in expected)
        {
            Assert.True(actual.ContainsKey(name), $"Missing property {name}");
            Assert.Equal(value, actual[name]);
        }
    }
}
