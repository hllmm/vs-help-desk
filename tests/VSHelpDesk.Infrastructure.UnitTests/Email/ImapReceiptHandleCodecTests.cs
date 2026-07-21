using VSHelpDesk.Application.Abstractions.Email;
using VSHelpDesk.Infrastructure.Email;

namespace VSHelpDesk.Infrastructure.UnitTests.Email;

public sealed class ImapReceiptHandleCodecTests
{
    [Fact]
    public void ReceiptCodec_RoundTripsAccountFolderValidityAndUid()
    {
        var coordinates = new ImapReceiptCoordinates(
            AccountId: "greenmail-support",
            Folder: "INBOX",
            UidValidity: 17u,
            Uid: 42u);

        var encoded = ImapReceiptHandleCodec.Encode(coordinates);

        Assert.Equal("imap\0greenmail-support\0INBOX\017\042", encoded);

        var decoded = ImapReceiptHandleCodec.Decode(
            new EmailReceiptHandle(EmailReceiptKind.Imap, encoded),
            expectedAccountId: "greenmail-support",
            expectedFolder: "INBOX");

        Assert.Equal(coordinates, decoded);
    }

    [Fact]
    public void ReceiptCodec_PreservesFolderCase_AndRejectsControlCharacters()
    {
        var coordinates = new ImapReceiptCoordinates(
            AccountId: "acct-A",
            Folder: "Archive/Important",
            UidValidity: 1u,
            Uid: 9u);

        var encoded = ImapReceiptHandleCodec.Encode(coordinates);
        Assert.Contains("Archive/Important", encoded, StringComparison.Ordinal);
        Assert.DoesNotContain("archive/important", encoded, StringComparison.Ordinal);

        var decoded = ImapReceiptHandleCodec.Decode(
            new EmailReceiptHandle(EmailReceiptKind.Imap, encoded),
            expectedAccountId: "acct-A",
            expectedFolder: "Archive/Important");
        Assert.Equal("Archive/Important", decoded.Folder);

        Assert.Throws<ArgumentException>(() =>
            ImapReceiptHandleCodec.Decode(
                new EmailReceiptHandle(EmailReceiptKind.Imap, encoded),
                expectedAccountId: "acct-A",
                expectedFolder: "archive/important"));

        Assert.Throws<ArgumentException>(() =>
            ImapReceiptHandleCodec.Encode(
                new ImapReceiptCoordinates("acct\0bad", "INBOX", 1u, 1u)));

        Assert.Throws<ArgumentException>(() =>
            ImapReceiptHandleCodec.Encode(
                new ImapReceiptCoordinates("acct", "IN\nBOX", 1u, 1u)));

        Assert.Throws<ArgumentException>(() =>
            ImapReceiptHandleCodec.Decode(
                new EmailReceiptHandle(EmailReceiptKind.Fake, "fake\0x"),
                expectedAccountId: "acct",
                expectedFolder: "INBOX"));
    }
}
