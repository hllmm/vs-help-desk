using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MimeKit;
using VSHelpDesk.Application.Abstractions.Email;
using VSHelpDesk.Infrastructure.Email;

namespace VSHelpDesk.Infrastructure.UnitTests.Email;

public sealed class MailKitImapMailboxClientGatewayTests
{
    private const long MiB = 1024 * 1024;

    [Fact]
    public async Task AggregateBudget_7MiB_Sequence_With50MiB_Budget_Skips2MiB_AndFetches8Messages()
    {
        // Sizes: 7,7,7,7,7,7,7,2,1 with 50 MiB budget => 7*7=49, 2 would exceed to 51, 1 fits to 50 => total 8 fetches, 2 MiB never fetched
        var sizesMiB = new[] { 7, 7, 7, 7, 7, 7, 7, 2, 1 };
        var uids = Enumerable.Range(1, sizesMiB.Length).Select(i => (uint)i).ToList();
        var sizeMap = uids.Zip(sizesMiB, (uid, mib) => (uid, size: (uint?)(mib * MiB))).ToDictionary(x => x.uid, x => x.size);

        var quota = new MailboxQuotaOptions
        {
            MaxMessagesPerRun = 100,
            MaxAttachmentsPerMessage = 10,
            MaxAggregateBytesPerRun = 50 * MiB,
            MaxRawMessageBytes = 10 * MiB // allow 7 MiB to be Ready
        };

        var gateway = new CountingGateway
        {
            Uids = uids,
            Sizes = sizeMap,
            UidValidityValue = 123u
        };

        var client = new MailKitImapMailboxClient(
            Options.Create(new EmailOptions
            {
                ReceiverMode = "Imap",
                ImapHost = "localhost",
                ImapPort = 993,
                ImapSecurityMode = MailTransportSecurityMode.None,
                ImapUsername = "test",
                ImapPassword = "test",
                ImapAccountId = "test-account",
                ImapFolder = "INBOX",
                SmtpHost = "localhost",
                SmtpPort = 25
            }),
            NullLogger<MailKitImapMailboxClient>.Instance,
            gateway,
            Options.Create(quota));

        var results = await client.FetchUnreadAsync().ToListAsync();

        Assert.Equal(9, results.Count);
        // First 7 should be Ready
        for (var i = 0; i < 7; i++)
        {
            Assert.Equal(ImapItemDisposition.Ready, results[i].Disposition);
            Assert.NotNull(results[i].Message);
        }

        // 8th (2 MiB) should be AggregateBudgetExceeded
        Assert.Equal(ImapItemDisposition.AggregateBudgetExceeded, results[7].Disposition);
        Assert.Null(results[7].Message);
        Assert.Equal(2 * MiB, results[7].RawSize);

        // 9th (1 MiB) should be Ready (49+1=50 fits)
        Assert.Equal(ImapItemDisposition.Ready, results[8].Disposition);
        Assert.NotNull(results[8].Message);

        // 2 MiB UID (8) never fetched/decoded
        Assert.DoesNotContain(8u, gateway.FetchedUids);
        Assert.Contains(1u, gateway.FetchedUids);
        Assert.Contains(9u, gateway.FetchedUids);
        // Total full-message fetch count is 8 (7 + 1)
        Assert.Equal(8, gateway.FetchMessageCallCount);
        // Also verify aggregate sum: 7*7 +1 =50 MiB worth of Ready messages
        var readyCount = results.Count(r => r.Disposition == ImapItemDisposition.Ready);
        Assert.Equal(8, readyCount);
    }

    [Fact]
    public async Task WhenRemainingAggregateBudget_IsZero_YieldsBudgetExceededWithoutCallingFetchRawBounded()
    {
        // Setup aggregate already at 50 MiB, next SIZE-null item should be BudgetExceeded without fetch
        var quota = new MailboxQuotaOptions
        {
            MaxMessagesPerRun = 10,
            MaxAttachmentsPerMessage = 10,
            MaxAggregateBytesPerRun = 5 * MiB,
            MaxRawMessageBytes = 5 * MiB
        };

        // First item will fill 5 MiB via SIZE-known, second item has SIZE null and should be BudgetExceeded without fetch
        var gateway = new CountingGateway
        {
            Uids = [1, 2],
            Sizes = new Dictionary<uint, uint?> { [1] = 5u * (uint)MiB, [2] = null },
            UidValidityValue = 1u,
            RawResponses = new Dictionary<uint, (byte[] Bytes, long Read)>
            {
                // If FetchRawBounded were called for uid 2, it would return 1 MiB, but we assert it's never called
                [2] = (Encoding.UTF8.GetBytes("should-not-be-called"), 1024)
            }
        };

        var client = new MailKitImapMailboxClient(
            Options.Create(new EmailOptions
            {
                ReceiverMode = "Imap",
                ImapHost = "localhost",
                ImapPort = 993,
                ImapSecurityMode = MailTransportSecurityMode.None,
                ImapUsername = "test",
                ImapPassword = "test",
                ImapAccountId = "test-account",
                ImapFolder = "INBOX",
                SmtpHost = "localhost",
                SmtpPort = 25
            }),
            NullLogger<MailKitImapMailboxClient>.Instance,
            gateway,
            Options.Create(quota));

        var results = await client.FetchUnreadAsync().ToListAsync();

        Assert.Equal(2, results.Count);
        Assert.Equal(ImapItemDisposition.Ready, results[0].Disposition);
        Assert.Equal(ImapItemDisposition.AggregateBudgetExceeded, results[1].Disposition);
        Assert.Null(results[1].Message);
        // FetchRawBounded should NOT have been called for uid 2 because remaining <=0
        Assert.DoesNotContain(2u, gateway.FetchRawBoundedUids);
        Assert.Equal(0, gateway.FetchRawBoundedCallCountForUid(2));
    }

    [Fact]
    public async Task SizeNull_BoundedFetch_Ready_WhenUnderBothLimits()
    {
        var quota = new MailboxQuotaOptions
        {
            MaxMessagesPerRun = 10,
            MaxAttachmentsPerMessage = 10,
            MaxAggregateBytesPerRun = 50 * MiB,
            MaxRawMessageBytes = 5 * MiB
        };

        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress("A", "a@test.com"));
        mime.Subject = "Test";
        mime.Body = new TextPart("plain") { Text = "hello" };
        using var ms = new MemoryStream();
        mime.WriteTo(ms);
        var raw = ms.ToArray();

        var gateway = new CountingGateway
        {
            Uids = [1],
            Sizes = new Dictionary<uint, uint?> { [1] = null },
            UidValidityValue = 1u,
            RawResponses = new Dictionary<uint, (byte[] Bytes, long Read)>
            {
                [1] = (raw, raw.Length)
            }
        };

        var client = new MailKitImapMailboxClient(
            Options.Create(new EmailOptions
            {
                ReceiverMode = "Imap",
                ImapHost = "localhost",
                ImapPort = 993,
                ImapSecurityMode = MailTransportSecurityMode.None,
                ImapUsername = "test",
                ImapPassword = "test",
                ImapAccountId = "test-account",
                ImapFolder = "INBOX",
                SmtpHost = "localhost",
                SmtpPort = 25
            }),
            NullLogger<MailKitImapMailboxClient>.Instance,
            gateway,
            Options.Create(quota));

        var results = await client.FetchUnreadAsync().ToListAsync();
        var item = Assert.Single(results);
        Assert.Equal(ImapItemDisposition.Ready, item.Disposition);
        Assert.NotNull(item.Message);
        Assert.Equal(raw.Length, item.RawSize);
        Assert.Single(gateway.FetchRawBoundedUids);
    }

    [Fact]
    public async Task SizeNull_BoundedFetch_RawMessageTooLarge_WhenExceedsMaxRaw()
    {
        var quota = new MailboxQuotaOptions
        {
            MaxMessagesPerRun = 10,
            MaxAttachmentsPerMessage = 10,
            MaxAggregateBytesPerRun = 50 * MiB,
            MaxRawMessageBytes = 1 * MiB
        };

        var gateway = new CountingGateway
        {
            Uids = [1],
            Sizes = new Dictionary<uint, uint?> { [1] = null },
            UidValidityValue = 1u,
            RawResponses = new Dictionary<uint, (byte[] Bytes, long Read)>
            {
                [1] = (Array.Empty<byte>(), 2 * MiB) // read exceeds MaxRaw
            }
        };

        var client = CreateClient(gateway, quota);

        var results = await client.FetchUnreadAsync().ToListAsync();
        var item = Assert.Single(results);
        Assert.Equal(ImapItemDisposition.RawMessageTooLarge, item.Disposition);
        Assert.Null(item.Message);
        Assert.Equal(2 * MiB, item.RawSize);
    }

    [Fact]
    public async Task SizeNull_BoundedFetch_AggregateBudgetExceeded_WhenExceedsRemaining()
    {
        var quota = new MailboxQuotaOptions
        {
            MaxMessagesPerRun = 10,
            MaxAttachmentsPerMessage = 10,
            MaxAggregateBytesPerRun = 2 * MiB,
            MaxRawMessageBytes = 5 * MiB
        };

        // First SIZE-known 1 MiB will be Ready (aggregate 1), second SIZE-null 2 MiB will exceed remaining 1 MiB => BudgetExceeded
        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress("A", "a@test.com"));
        mime.Subject = "First";
        mime.Body = new TextPart("plain") { Text = "hello" };
        using var ms = new MemoryStream();
        mime.WriteTo(ms);
        var raw1 = ms.ToArray();

        var gateway = new CountingGateway
        {
            Uids = [1, 2],
            Sizes = new Dictionary<uint, uint?> { [1] = 1u * (uint)MiB, [2] = null },
            UidValidityValue = 1u,
            RawResponses = new Dictionary<uint, (byte[] Bytes, long Read)>
            {
                // Even if raw is small, remaining is 1 MiB, but read is 2 MiB > remaining => BudgetExceeded
                [2] = (Array.Empty<byte>(), 2 * MiB)
            }
        };

        // Mock FetchMessage for first uid to succeed
        gateway.FetchMessageResponses[1] = mime;

        var client = CreateClient(gateway, quota);
        var results = await client.FetchUnreadAsync().ToListAsync();

        Assert.Equal(2, results.Count);
        Assert.Equal(ImapItemDisposition.Ready, results[0].Disposition);
        Assert.Equal(ImapItemDisposition.AggregateBudgetExceeded, results[1].Disposition);
        Assert.Null(results[1].Message);
        Assert.Equal(2 * MiB, results[1].RawSize);
        Assert.Contains(2u, gateway.FetchRawBoundedUids);
    }

    [Fact]
    public async Task SizeNull_BoundedFetch_SizeUnavailable_WhenGatewayThrows()
    {
        var quota = new MailboxQuotaOptions
        {
            MaxMessagesPerRun = 10,
            MaxAttachmentsPerMessage = 10,
            MaxAggregateBytesPerRun = 50 * MiB,
            MaxRawMessageBytes = 5 * MiB
        };

        var gateway = new CountingGateway
        {
            Uids = [1],
            Sizes = new Dictionary<uint, uint?> { [1] = null },
            UidValidityValue = 1u,
            ThrowOnFetchRawForUids = new HashSet<uint> { 1 }
        };

        var client = CreateClient(gateway, quota);
        var results = await client.FetchUnreadAsync().ToListAsync();
        var item = Assert.Single(results);
        Assert.Equal(ImapItemDisposition.SizeUnavailable, item.Disposition);
        Assert.Null(item.Message);
        Assert.Null(item.RawSize);
    }

    [Fact]
    public async Task MarkSeenAsync_UsesGatewaySeam_DoesNotConnectToLocalhost()
    {
        var gateway = new CountingGateway
        {
            Uids = [],
            Sizes = new Dictionary<uint, uint?>(),
            UidValidityValue = 42u
        };

        var client = new MailKitImapMailboxClient(
            Options.Create(new EmailOptions
            {
                ReceiverMode = "Imap",
                ImapHost = "should-not-connect",
                ImapPort = 9999,
                ImapSecurityMode = MailTransportSecurityMode.None,
                ImapUsername = "test",
                ImapPassword = "test",
                ImapAccountId = "test-account",
                ImapFolder = "INBOX",
                SmtpHost = "localhost",
                SmtpPort = 25
            }),
            NullLogger<MailKitImapMailboxClient>.Instance,
            gateway,
            Options.Create(new MailboxQuotaOptions()));

        // Should delegate to gateway, not try to connect to should-not-connect:9999
        await client.MarkSeenAsync(42u, 123u, CancellationToken.None);

        Assert.Single(gateway.MarkedUids);
        Assert.Equal(123u, gateway.MarkedUids[0]);
    }

    private static MailKitImapMailboxClient CreateClient(CountingGateway gateway, MailboxQuotaOptions quota) =>
        new(
            Options.Create(new EmailOptions
            {
                ReceiverMode = "Imap",
                ImapHost = "localhost",
                ImapPort = 993,
                ImapSecurityMode = MailTransportSecurityMode.None,
                ImapUsername = "test",
                ImapPassword = "test",
                ImapAccountId = "test-account",
                ImapFolder = "INBOX",
                SmtpHost = "localhost",
                SmtpPort = 25
            }),
            NullLogger<MailKitImapMailboxClient>.Instance,
            gateway,
            Options.Create(quota));

    private sealed class CountingGateway : IImapFolderGateway
    {
        public List<uint> Uids { get; init; } = [];
        public Dictionary<uint, uint?> Sizes { get; init; } = new();
        public uint UidValidityValue { get; init; } = 1u;
        public Dictionary<uint, MimeMessage> FetchMessageResponses { get; } = new();
        public Dictionary<uint, (byte[] Bytes, long Read)> RawResponses { get; init; } = new();
        public HashSet<uint> ThrowOnFetchRawForUids { get; init; } = [];

        public List<uint> FetchedUids { get; } = [];
        public List<uint> FetchRawBoundedUids { get; } = [];
        public List<uint> MarkedUids { get; } = [];
        public int FetchMessageCallCount => FetchedUids.Count;

        public uint UidValidity => UidValidityValue;

        public Task<IReadOnlyList<uint>> SearchUnseenAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<uint>>(Uids);

        public Task<Dictionary<uint, uint?>> FetchSizesAsync(IReadOnlyList<uint> uids, CancellationToken ct) => Task.FromResult(Sizes);

        public Task<MimeMessage> FetchMessageAsync(uint uid, CancellationToken ct)
        {
            FetchedUids.Add(uid);
            if (FetchMessageResponses.TryGetValue(uid, out var msg))
            {
                return Task.FromResult(msg);
            }

            var mime = new MimeMessage();
            mime.From.Add(new MailboxAddress("Test", "test@example.com"));
            mime.Subject = $"Subject {uid}";
            mime.Body = new TextPart("plain") { Text = $"Body {uid}" };
            mime.MessageId = $"msg-{uid}@test.com";
            return Task.FromResult(mime);
        }

        public Task<(byte[] Bytes, long BytesRead)> FetchRawBoundedAsync(uint uid, long limit, CancellationToken ct)
        {
            FetchRawBoundedUids.Add(uid);
            if (ThrowOnFetchRawForUids.Contains(uid))
            {
                throw new NotSupportedException("simulated SizeUnavailable");
            }

            if (RawResponses.TryGetValue(uid, out var resp))
            {
                return Task.FromResult(resp);
            }

            // default: return small message under limit
            var mime = new MimeMessage();
            mime.From.Add(new MailboxAddress("Test", "test@example.com"));
            mime.Subject = $"Raw {uid}";
            mime.Body = new TextPart("plain") { Text = $"Raw body {uid}" };
            using var ms = new MemoryStream();
            mime.WriteTo(ms);
            var bytes = ms.ToArray();
            return Task.FromResult((bytes, (long)bytes.Length));
        }

        public Task MarkSeenAsync(uint uid, CancellationToken ct)
        {
            MarkedUids.Add(uid);
            return Task.CompletedTask;
        }

        public int FetchRawBoundedCallCountForUid(uint uid) => FetchRawBoundedUids.Count(id => id == uid);
    }
}
