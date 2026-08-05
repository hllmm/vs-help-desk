using VSHelpDesk.Application.Abstractions.Email;
using VSHelpDesk.Application.Features.MailProcessing;

namespace VSHelpDesk.Application.UnitTests.Features.MailProcessing;

public sealed class InboundEmailNormalizerTests
{
    [Fact]
    public void MissingSender_IsQuarantinedWithBoundedNote()
    {
        var result = InboundEmailNormalizer.Normalize(Mail(
            fromAddress: null,
            fromDisplayName: "Someone",
            subject: "Help",
            body: "Body"));

        Assert.Equal(InboundEmailPolicyOutcome.Quarantine, result.Outcome);
        Assert.Null(result.Email);
        Assert.False(string.IsNullOrWhiteSpace(result.ProcessingNote));
        Assert.True(result.ProcessingNote!.Length <= InboundMailLimits.MaxProcessingNoteLength);
        Assert.Equal("<msg@test>", result.Identity.IdempotencyKey);
    }

    [Fact]
    public void SenderWithCrLf_IsQuarantined()
    {
        var result = InboundEmailNormalizer.Normalize(Mail(
            fromAddress: "evil@example.test\r\nBcc: other@example.test",
            fromDisplayName: "Evil",
            subject: "Help",
            body: "Body"));

        Assert.Equal(InboundEmailPolicyOutcome.Quarantine, result.Outcome);
        Assert.Null(result.Email);
        Assert.False(string.IsNullOrWhiteSpace(result.ProcessingNote));
        Assert.True(result.ProcessingNote!.Length <= InboundMailLimits.MaxProcessingNoteLength);
    }

    [Fact]
    public void SubjectNameAndBody_AreNormalizedAtPersistenceLimits()
    {
        var longSubject = new string('S', InboundMailLimits.MaxSubjectLength + 40);
        var longName = new string('N', InboundMailLimits.MaxDisplayNameLength + 25);
        var longBody = new string('B', InboundMailLimits.MaxBodyLength + 10);

        var result = InboundEmailNormalizer.Normalize(Mail(
            fromAddress: "customer@example.test",
            fromDisplayName: longName,
            subject: longSubject,
            body: longBody));

        Assert.Equal(InboundEmailPolicyOutcome.Process, result.Outcome);
        Assert.NotNull(result.Email);
        Assert.Equal(InboundMailLimits.MaxSubjectLength, result.Email!.Subject.Length);
        Assert.Equal(new string('S', InboundMailLimits.MaxSubjectLength), result.Email.Subject);
        Assert.Equal(InboundMailLimits.MaxDisplayNameLength, result.Email.FromDisplayName.Length);
        Assert.Equal(new string('N', InboundMailLimits.MaxDisplayNameLength), result.Email.FromDisplayName);
        Assert.Equal(InboundMailLimits.MaxBodyLength, result.Email.Body.Length);
        Assert.Equal("customer@example.test", result.Email.FromAddress);
        Assert.Null(result.ProcessingNote);
    }

    [Fact]
    public void BlankSubjectAndBody_UseTurkishPlaceholders()
    {
        var result = InboundEmailNormalizer.Normalize(Mail(
            fromAddress: "customer@example.test",
            fromDisplayName: "  ",
            subject: "   ",
            body: "\t"));

        Assert.Equal(InboundEmailPolicyOutcome.Process, result.Outcome);
        Assert.NotNull(result.Email);
        Assert.Equal(InboundMailLimits.EmptySubjectPlaceholder, result.Email!.Subject);
        Assert.Equal(InboundMailLimits.EmptyBodyPlaceholder, result.Email.Body);
        Assert.Equal("customer@example.test", result.Email.FromDisplayName);
        Assert.Equal("Konusuz e-posta", result.Email.Subject);
        Assert.Equal("İleti içeriği bulunamadı.", result.Email.Body);
    }

    [Fact]
    public void Normalize_WhenNoAuthVerdict_QuarantinesSpoofedReply()
    {
        var email = new IncomingEmail(
            "<id@host>",
            new EmailReceiptHandle(EmailReceiptKind.Fake, "fake\0x"),
            "attacker@evil.com",
            "Attacker",
            "Re: [VS-000042] test",
            "body",
            false,
            DateTime.UtcNow,
            Array.Empty<IncomingEmailAttachment>());
        // No AuthenticationResults -> untrusted
        var result = InboundEmailNormalizer.Normalize(email);
        Assert.Equal(InboundEmailPolicyOutcome.Quarantine, result.Outcome);
        Assert.Contains("authentication failed", result.ProcessingNote!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Normalize_WhenDmarcPass_AllowsReplyWithTicketNumber()
    {
        var email = new IncomingEmail(
            "<id2@host>",
            new EmailReceiptHandle(EmailReceiptKind.Fake, "fake\0y"),
            "customer@example.test",
            "Customer",
            "Re: [VS-000042] test",
            "body",
            false,
            DateTime.UtcNow,
            Array.Empty<IncomingEmailAttachment>(),
            AuthenticationResults: "mx.test; dmarc=pass header.from=example.test");
        var result = InboundEmailNormalizer.Normalize(email);
        Assert.Equal(InboundEmailPolicyOutcome.Process, result.Outcome);
        Assert.NotNull(result.Email);
    }

    [Fact]
    public void Normalize_WhenAuthHeaderMissingButNoTicketNumber_StillProcesses()
    {
        var result = InboundEmailNormalizer.Normalize(Mail(
            fromAddress: "customer@example.test",
            fromDisplayName: "Customer",
            subject: "Help no ticket",
            body: "Body"));
        Assert.Equal(InboundEmailPolicyOutcome.Process, result.Outcome);
    }

    [Fact]
    public void Normalize_WhenDmarcFail_QuarantinesReply()
    {
        var email = new IncomingEmail(
            "<id3@host>",
            new EmailReceiptHandle(EmailReceiptKind.Fake, "fake\0z"),
            "attacker@evil.com",
            "Attacker",
            "Re: [VS-000042] test",
            "body",
            false,
            DateTime.UtcNow,
            Array.Empty<IncomingEmailAttachment>(),
            AuthenticationResults: "mx.test; dmarc=fail spf=fail");
        var result = InboundEmailNormalizer.Normalize(email);
        Assert.Equal(InboundEmailPolicyOutcome.Quarantine, result.Outcome);
    }

    private static IncomingEmail Mail(
        string? fromAddress,
        string? fromDisplayName,
        string? subject,
        string? body,
        string? authenticationResults = null) =>
        new(
            MessageId: "<msg@test>",
            ReceiptHandle: new EmailReceiptHandle(EmailReceiptKind.Fake, "fake\0fixture-norm"),
            FromAddress: fromAddress,
            FromDisplayName: fromDisplayName,
            Subject: subject,
            Body: body,
            IsHtml: false,
            ReceivedAt: new DateTime(2026, 7, 20, 12, 0, 0, DateTimeKind.Utc),
            Attachments: Array.Empty<IncomingEmailAttachment>(),
            AuthenticationResults: authenticationResults);
}
