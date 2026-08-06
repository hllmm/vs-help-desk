using System.Collections.Generic;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using VSHelpDesk.Application.Abstractions.Email;
using VSHelpDesk.Infrastructure.Email;
using VSHelpDesk.Infrastructure.Storage;

namespace VSHelpDesk.Infrastructure.UnitTests.Email;

public sealed class ImapEmailReceiverAggregateTests
{
    [Fact]
    public async Task Receiver_maps_AggregateExceeded_to_metadata_only()
    {
        var item = new ImapMailboxItem(100, 42, null, 2 * 1024 * 1024, ImapItemDisposition.AggregateBudgetExceeded);
        var fakeClient = new FakeMailboxClient(new[] { item });
        var receiver = CreateReceiver(fakeClient);
        var list = new List<IncomingEmail>();
        await foreach (var m in receiver.FetchUnreadAsync(CancellationToken.None)) list.Add(m);
        Assert.Null(list[0].MessageId);
        Assert.Empty(list[0].Attachments);
        Assert.Equal(ImapItemDisposition.AggregateBudgetExceeded, list[0].Disposition);
        var decoded = ImapReceiptHandleCodec.Decode(list[0].ReceiptHandle, "test-account", "INBOX");
        Assert.Equal(42u, decoded.Uid);
        Assert.Equal(100u, decoded.UidValidity);
    }

    private static ImapEmailReceiver CreateReceiver(IImapMailboxClient client)
    {
        var options = Options.Create(new EmailOptions
        {
            ReceiverMode = "Imap",
            ImapHost = "localhost",
            ImapPort = 3143,
            ImapSecurityMode = MailTransportSecurityMode.None,
            ImapUsername = "support@vshelpdesk.test",
            ImapPassword = "test",
            ImapAccountId = "test-account",
            ImapFolder = "INBOX",
            SmtpHost = "localhost",
            SmtpPort = 3025,
            SupportMailboxAddress = "support@vshelpdesk.test"
        });
        var fileStorageOptions = Options.Create(new FileStorageOptions
        {
            RootPath = "storage",
            MaxFileSizeBytes = 10 * 1024 * 1024
        });
        return new ImapEmailReceiver(
            options,
            fileStorageOptions,
            client,
            new HtmlToPlainTextConverter(),
            NullLogger<ImapEmailReceiver>.Instance);
    }

    private sealed class FakeMailboxClient : IImapMailboxClient
    {
        private readonly IReadOnlyList<ImapMailboxItem> _items;
        public FakeMailboxClient(IReadOnlyList<ImapMailboxItem> items) => _items = items;
        public async IAsyncEnumerable<ImapMailboxItem> FetchUnreadAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            foreach (var i in _items) { yield return i; await Task.Yield(); }
        }
        public Task MarkSeenAsync(uint expectedUidValidity, uint uid, CancellationToken cancellationToken) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
