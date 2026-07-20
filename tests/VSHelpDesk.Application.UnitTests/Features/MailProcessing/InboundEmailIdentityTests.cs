using System.Security.Cryptography;
using System.Text;
using VSHelpDesk.Application.Abstractions.Email;
using VSHelpDesk.Application.Features.MailProcessing;

namespace VSHelpDesk.Application.UnitTests.Features.MailProcessing;

public sealed class InboundEmailIdentityTests
{
    [Fact]
    public void ValidMessageId_IsTrimmedWithoutChangingCase()
    {
        var email = Mail(
            messageId: "  <AbC@Example.TEST>  ",
            receiptValue: "fake\0fixture-trim");

        var identity = InboundEmailIdentityFactory.Create(email);

        Assert.Equal("<AbC@Example.TEST>", identity.IdempotencyKey);
        Assert.Equal("<AbC@Example.TEST>", identity.SourceMessageId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("broken@example.test")]
    [InlineData("<two@@example.test>")]
    public void InvalidMessageId_UsesStableReceiptHash(string? messageId)
    {
        var receipt = "fake\0fixture-001";
        var email = Mail(messageId, receipt);

        var identity = InboundEmailIdentityFactory.Create(email);

        var expected =
            "receipt:fake:" +
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(receipt)))
                .ToLowerInvariant();

        Assert.Equal(expected, identity.IdempotencyKey);
        Assert.Null(identity.SourceMessageId);
    }

    [Fact]
    public void OverlongMessageId_UsesStableReceiptHash()
    {
        var receipt = "fake\0fixture-overlong";
        var overlong = "<" + new string('a', 500) + "@" + new string('b', 500) + ">";
        Assert.True(overlong.Length > InboundMailLimits.MaxMessageIdLength);

        var identity = InboundEmailIdentityFactory.Create(Mail(overlong, receipt));

        var expected =
            "receipt:fake:" +
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(receipt)))
                .ToLowerInvariant();

        Assert.Equal(expected, identity.IdempotencyKey);
        Assert.Null(identity.SourceMessageId);
    }

    [Fact]
    public void ImapReceipt_UsesImapKindInHashKey()
    {
        var receipt = "imap\0acct\0INBOX\01\042";
        var email = new IncomingEmail(
            MessageId: null,
            ReceiptHandle: new EmailReceiptHandle(EmailReceiptKind.Imap, receipt),
            FromAddress: "a@b.test",
            FromDisplayName: null,
            Subject: "s",
            Body: "b",
            IsHtml: false,
            ReceivedAt: DateTime.UtcNow,
            Attachments: Array.Empty<IncomingEmailAttachment>());

        var identity = InboundEmailIdentityFactory.Create(email);

        var expected =
            "receipt:imap:" +
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(receipt)))
                .ToLowerInvariant();

        Assert.Equal(expected, identity.IdempotencyKey);
    }

    private static IncomingEmail Mail(string? messageId, string receiptValue) =>
        new(
            MessageId: messageId,
            ReceiptHandle: new EmailReceiptHandle(EmailReceiptKind.Fake, receiptValue),
            FromAddress: "customer@example.test",
            FromDisplayName: "Customer",
            Subject: "Subject",
            Body: "Body",
            IsHtml: false,
            ReceivedAt: DateTime.UtcNow,
            Attachments: Array.Empty<IncomingEmailAttachment>());
}
