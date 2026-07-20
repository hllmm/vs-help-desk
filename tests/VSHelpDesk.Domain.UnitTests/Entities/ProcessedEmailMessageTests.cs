using VSHelpDesk.Domain.Entities;
using VSHelpDesk.Domain.Enums;

namespace VSHelpDesk.Domain.UnitTests.Entities;

public sealed class ProcessedEmailMessageTests
{
    private static readonly DateTime T0 =
        new(2026, 7, 20, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void ForCreatedTicket_StartsPendingAndDueImmediately()
    {
        var ticketId = Guid.NewGuid();
        var row = ProcessedEmailMessage.ForCreatedTicket(
            "<key@test>",
            "<key@test>",
            T0,
            ticketId);

        Assert.Equal(ProcessedEmailDisposition.CreatedTicket, row.Disposition);
        Assert.Equal(AcknowledgementStatus.Pending, row.AcknowledgementStatus);
        Assert.Equal(T0, row.AcknowledgementNextAttemptAt);
        Assert.True(row.IsAcknowledgementDue(T0));
        Assert.Equal(0, row.AcknowledgementAttempts);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 5)]
    [InlineData(3, 15)]
    [InlineData(4, 60)]
    [InlineData(8, 60)]
    public void RecordAcknowledgementFailure_UsesBoundedBackoff(
        int failureNumber,
        int expectedMinutes)
    {
        var row = ProcessedEmailMessage.ForCreatedTicket(
            "<key@test>",
            "<key@test>",
            T0,
            Guid.NewGuid());
        var now = T0;

        for (var attempt = 1; attempt <= failureNumber; attempt++)
        {
            now = now.AddMinutes(2);
            row.RecordAcknowledgementFailure(now, "SMTP unavailable");
        }

        Assert.Equal(failureNumber, row.AcknowledgementAttempts);
        Assert.Equal(
            now.AddMinutes(expectedMinutes),
            row.AcknowledgementNextAttemptAt);
        Assert.Equal(AcknowledgementStatus.Failed, row.AcknowledgementStatus);
    }

    [Fact]
    public void RecordAcknowledgementSent_MarksSentAndClearsRetry()
    {
        var row = ProcessedEmailMessage.ForCreatedTicket(
            "<key@test>",
            "<key@test>",
            T0,
            Guid.NewGuid());
        var attemptedAt = T0.AddMinutes(3);

        row.RecordAcknowledgementSent(attemptedAt);

        Assert.Equal(AcknowledgementStatus.Sent, row.AcknowledgementStatus);
        Assert.Equal(1, row.AcknowledgementAttempts);
        Assert.Equal(attemptedAt, row.AcknowledgementLastAttemptAt);
        Assert.Equal(attemptedAt, row.AcknowledgementSentAt);
        Assert.Null(row.AcknowledgementNextAttemptAt);
        Assert.Null(row.AcknowledgementLastError);
        Assert.False(row.IsAcknowledgementDue(attemptedAt.AddHours(1)));
    }

    [Fact]
    public void ForAppendedReply_UsesNotRequired()
    {
        var ticketId = Guid.NewGuid();
        var row = ProcessedEmailMessage.ForAppendedReply(
            "<reply@test>",
            "<reply@test>",
            T0,
            ticketId);

        Assert.Equal(ProcessedEmailDisposition.AppendedReply, row.Disposition);
        Assert.Equal(AcknowledgementStatus.NotRequired, row.AcknowledgementStatus);
        Assert.Null(row.AcknowledgementNextAttemptAt);
        Assert.Equal(ticketId, row.TicketId);
        Assert.False(row.IsAcknowledgementDue(T0));
    }

    [Fact]
    public void ForQuarantine_TrimsProcessingNoteTo500Characters()
    {
        var note = new string('x', 600);
        var row = ProcessedEmailMessage.ForQuarantine(
            "<q@test>",
            "<q@test>",
            T0,
            note);

        Assert.Equal(ProcessedEmailDisposition.Quarantined, row.Disposition);
        Assert.Equal(AcknowledgementStatus.NotRequired, row.AcknowledgementStatus);
        Assert.Equal(500, row.ProcessingNote!.Length);
        Assert.Equal(new string('x', 500), row.ProcessingNote);
        Assert.Null(row.TicketId);
    }

    [Fact]
    public void RecordAcknowledgementFailure_TrimsErrorTo500Characters()
    {
        var row = ProcessedEmailMessage.ForCreatedTicket(
            "<key@test>",
            "<key@test>",
            T0,
            Guid.NewGuid());
        var error = new string('e', 600);

        row.RecordAcknowledgementFailure(T0.AddMinutes(1), error);

        Assert.Equal(500, row.AcknowledgementLastError!.Length);
        Assert.Equal(new string('e', 500), row.AcknowledgementLastError);
    }
}
