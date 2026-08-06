using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MimeKit;
using VSHelpDesk.Application.Abstractions.Email;
using VSHelpDesk.Infrastructure.Email;
using VSHelpDesk.Infrastructure.Storage;

namespace VSHelpDesk.Infrastructure.UnitTests.Email;

public sealed class ImapEmailReceiverAuthenticationTests
{
    [Fact]
    public async Task FetchUnread_WithDmarcPass_PassesThroughAuthenticationResults()
    {
        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress("Customer", "customer@example.test"));
        mime.Subject = "Re: [VS-000042] Hello";
        mime.MessageId = "auth-pass@example.test";
        mime.Headers.Add("Authentication-Results", "mx.example.test; dmarc=pass header.from=example.test; spf=pass");
        mime.Body = new TextPart("plain") { Text = "Body" };

        var client = new FakeImapMailboxClient
        {
            Items = [new ImapMailboxItem(UidValidity: 1u, Uid: 1u, Message: mime)]
        };

        var receiver = CreateReceiver(client);
        var unread = await receiver.FetchUnreadAsync().ToListAsync();
        var item = Assert.Single(unread);
        Assert.NotNull(item.AuthenticationVerdict);
        Assert.True(item.AuthenticationVerdict!.IsTrusted);
        Assert.Contains("dmarc=pass", item.AuthenticationVerdict!.RawHeader!, StringComparison.OrdinalIgnoreCase);
        Assert.True(item.AuthenticationVerdict!.DmarcPassed);
        var parsed = EmailAuthenticationResultParser.Parse(item.AuthenticationVerdict!.RawHeader);
        Assert.True(parsed.DmarcPassed);
        Assert.True(parsed.SpfPassed);
    }

    [Fact]
    public async Task FetchUnread_WithoutAuthenticationResults_IsNullUntrusted()
    {
        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress("Attacker", "attacker@evil.test"));
        mime.Subject = "Re: [VS-000042] Spoof";
        mime.MessageId = "no-auth@example.test";
        mime.Body = new TextPart("plain") { Text = "Body" };
        // No Authentication-Results header added

        var client = new FakeImapMailboxClient
        {
            Items = [new ImapMailboxItem(UidValidity: 2u, Uid: 2u, Message: mime)]
        };

        var receiver = CreateReceiver(client);
        var unread = await receiver.FetchUnreadAsync().ToListAsync();
        var item = Assert.Single(unread);
        Assert.NotNull(item.AuthenticationVerdict);
        Assert.False(item.AuthenticationVerdict!.IsTrusted);
        Assert.Null(item.AuthenticationVerdict!.RawHeader);
        var parsed = EmailAuthenticationResultParser.Parse(item.AuthenticationVerdict!.RawHeader);
        Assert.False(parsed.DmarcPassed);
    }

    [Fact]
    public async Task FetchUnread_WithDmarcFail_IsNotTrusted()
    {
        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress("Attacker", "attacker@evil.test"));
        mime.Subject = "Re: [VS-000042] Spoof";
        mime.MessageId = "fail-auth@example.test";
        mime.Headers.Add("Authentication-Results", "mx.example.test; dmarc=fail header.from=evil.test; spf=fail");
        mime.Body = new TextPart("plain") { Text = "Body" };

        var client = new FakeImapMailboxClient
        {
            Items = [new ImapMailboxItem(UidValidity: 3u, Uid: 3u, Message: mime)]
        };

        var receiver = CreateReceiver(client);
        var unread = await receiver.FetchUnreadAsync().ToListAsync();
        var item = Assert.Single(unread);
        Assert.NotNull(item.AuthenticationVerdict);
        Assert.False(item.AuthenticationVerdict!.IsTrusted);
        Assert.Contains("dmarc=fail", item.AuthenticationVerdict!.RawHeader!, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("dmarc=pass", true)]
    [InlineData("DMARC=PASS", true)]
    [InlineData("Dmarc=Pass", true)]
    [InlineData("dmarc=fail", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void EmailAuthenticationResultParser_CaseInsensitiveChecksDmarcPass(string? header, bool expectedTrusted)
    {
        var result = EmailAuthenticationResultParser.Parse(header);
        Assert.Equal(expectedTrusted, result.DmarcPassed);
        Assert.Equal(expectedTrusted, EmailAuthenticationResultParser.IsTrusted(header));
    }

    [Fact]
    public void EmailAuthenticationResultParser_WithMultipleHeaders_ExtractsPass()
    {
        var header = "mx1.example.test; spf=pass smtp.mailfrom=customer@example.test; dkim=pass header.d=example.test; dmarc=pass header.from=example.test";
        var result = EmailAuthenticationResultParser.Parse(header);
        Assert.True(result.DmarcPassed);
        Assert.True(result.SpfPassed);
        Assert.True(result.DkimPassed);
        Assert.Equal(header, result.RawHeader);
    }

    private static ImapEmailReceiver CreateReceiver(FakeImapMailboxClient client, long maxFileSizeBytes = 10 * 1024 * 1024)
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
            MaxFileSizeBytes = maxFileSizeBytes
        });

        return new ImapEmailReceiver(
            options,
            fileStorageOptions,
            client,
            new HtmlToPlainTextConverter(),
            NullLogger<ImapEmailReceiver>.Instance);
    }

    private sealed class FakeImapMailboxClient : IImapMailboxClient
    {
        public List<ImapMailboxItem> Items { get; init; } = [];
        public List<(uint ExpectedUidValidity, uint Uid)> Marked { get; } = [];

        public async IAsyncEnumerable<ImapMailboxItem> FetchUnreadAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken) { foreach(var i in Items){ yield return i; await Task.Yield(); } }

        public Task MarkSeenAsync(uint expectedUidValidity, uint uid, CancellationToken cancellationToken)
        {
            Marked.Add((expectedUidValidity, uid));
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
