using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Net.Smtp;
using MailKit.Search;
using MailKit.Security;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MimeKit;
using VSHelpDesk.Domain.Enums;
using VSHelpDesk.Infrastructure.Persistence;
using VSHelpDesk.WebAPI.IntegrationTests.Support;

namespace VSHelpDesk.WebAPI.IntegrationTests.Jobs;

/// <summary>
/// Real GreenMail IMAP + SMTP job path. Opt-in via VSHD_RUN_IMAP_TESTS=true.
/// </summary>
public sealed class ImapProcessIncomingEmailsApiTests
{
    private const string JobsApiKey = "integration-jobs-api-key-32chars!!";
    private const string SupportAddress = "support@vshelpdesk.test";
    private const string CustomerAddress = "customer@vshelpdesk.test";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    [GreenMailFact]
    public async Task ImapProcessIncomingEmails_CreatesTicket_MarksSeen_AndIsIdempotent()
    {
        var uniqueToken = Guid.NewGuid().ToString("N");
        var subject = $"GreenMail job {uniqueToken}";
        var body = $"GreenMail job body {uniqueToken}";
        // Keep ack recipient on the customer mailbox so GreenMail support INBOX
        // does not re-ingest the acknowledgement as a new support request.
        var fromAddress = CustomerAddress;

        await using var factory = CreateImapFactory();
        await ParkDueAcknowledgementsAsync(factory);

        Guid? ticketId = null;
        Guid? processedId = null;

        try
        {
            await SendCustomerMessageAsync(subject, body, fromAddress);

            using var client = factory.CreateClient();
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/jobs/process-incoming-emails");
            request.Headers.Add("X-Jobs-Api-Key", JobsApiKey);
            using var response = await client.SendAsync(request);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var payload = await response.Content.ReadFromJsonAsync<JobPayload>(JsonOptions);
            Assert.NotNull(payload);
            Assert.True(payload.FetchedCount >= 1, $"Expected at least one fetched mail, got {payload.FetchedCount}");
            Assert.True(payload.CreatedTickets >= 1, $"Expected ticket create, got {payload.CreatedTickets}");
            Assert.True(payload.AcknowledgementsSent >= 1, $"Expected ack, got {payload.AcknowledgementsSent}");
            Assert.Equal(0, payload.RetryableFailures);
            Assert.Contains(
                payload.CreatedTicketNumbers,
                number => number.StartsWith("VS-", StringComparison.Ordinal));

            string ticketNumber;
            string? idempotencyKey;
            uint uid;
            uint uidValidity;

            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var ticket = await db.Tickets
                    .SingleAsync(row => row.Subject == subject);
                ticketId = ticket.Id;
                ticketNumber = ticket.TicketNumber;
                Assert.Equal(fromAddress, ticket.CustomerEmail);
                Assert.Equal(TicketStatus.New, ticket.Status);

                var customerMessage = await db.TicketMessages
                    .Where(row => row.TicketId == ticket.Id)
                    .OrderBy(row => row.CreatedAt)
                    .FirstAsync();
                Assert.Equal(MessageSenderType.Customer, customerMessage.SenderType);
                Assert.Equal(body, customerMessage.Content);

                var processed = await db.ProcessedEmailMessages
                    .SingleAsync(row => row.TicketId == ticket.Id);
                processedId = processed.Id;
                idempotencyKey = processed.IdempotencyKey;
                Assert.Equal(ProcessedEmailDisposition.CreatedTicket, processed.Disposition);
                Assert.Equal(AcknowledgementStatus.Sent, processed.AcknowledgementStatus);
                Assert.True(processed.AcknowledgementAttempts >= 1);
                Assert.NotNull(processed.AcknowledgementSentAt);
            }

            (uidValidity, uid) = await AssertOriginalMessageIsSeenAsync(subject);

            // Second run: original receipt already Seen + durable idempotency key.
            using var secondRequest = new HttpRequestMessage(HttpMethod.Post, "/api/jobs/process-incoming-emails");
            secondRequest.Headers.Add("X-Jobs-Api-Key", JobsApiKey);
            using var secondResponse = await client.SendAsync(secondRequest);
            Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
            var secondPayload = await secondResponse.Content.ReadFromJsonAsync<JobPayload>(JsonOptions);
            Assert.NotNull(secondPayload);
            Assert.Equal(0, secondPayload.CreatedTickets);
            Assert.DoesNotContain(ticketNumber, secondPayload.CreatedTicketNumbers);

            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                Assert.Equal(
                    1,
                    await db.Tickets.CountAsync(row => row.Subject == subject));
                Assert.Equal(
                    1,
                    await db.ProcessedEmailMessages.CountAsync(row => row.IdempotencyKey == idempotencyKey));
                Assert.Equal(
                    1,
                    await db.TicketMessages.CountAsync(row => row.TicketId == ticketId));
            }

            // Receipt coordinates still Seen; not re-fetched as unread.
            await AssertUidStillSeenAsync(uidValidity, uid);
        }
        finally
        {
            await CleanupCreatedAsync(factory, ticketId, processedId);
        }
    }

    private static WebApplicationFactory<Program> CreateImapFactory()
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
            ?? throw new InvalidOperationException(
                "ConnectionStrings__DefaultConnection is required.");

        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            // UseSetting values are applied as host configuration (high priority) so
            // Auth/Jobs validation at Program startup sees them before empty appsettings defaults.
            builder.UseSetting("ConnectionStrings:DefaultConnection", connectionString);
            builder.UseSetting("Auth:SigningKey", "integration-test-signing-key-32-bytes!!");
            builder.UseSetting("Jobs:ApiKey", JobsApiKey);
            builder.UseSetting("SeedUser:Enabled", "false");
            builder.UseSetting("Email:ReceiverMode", "Imap");
            builder.UseSetting("Email:ImapHost", "localhost");
            builder.UseSetting("Email:ImapPort", "3143");
            builder.UseSetting("Email:ImapSecurityMode", "None");
            builder.UseSetting("Email:ImapUsername", SupportAddress);
            builder.UseSetting("Email:ImapPassword", "test");
            builder.UseSetting("Email:ImapAccountId", "greenmail-support");
            builder.UseSetting("Email:ImapFolder", "INBOX");
            builder.UseSetting("Email:SmtpHost", "localhost");
            builder.UseSetting("Email:SmtpPort", "3025");
            builder.UseSetting("Email:SmtpSecurityMode", "None");
            builder.UseSetting("Email:SupportMailboxAddress", SupportAddress);
            builder.UseSetting("Email:SupportMailboxDisplayName", "VS Help Desk");
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["ConnectionStrings:DefaultConnection"] = connectionString,
                        ["Auth:SigningKey"] =
                            "integration-test-signing-key-32-bytes!!",
                        ["Jobs:ApiKey"] = JobsApiKey,
                        ["SeedUser:Enabled"] = "false",
                        ["Email:ReceiverMode"] = "Imap",
                        ["Email:ImapHost"] = "localhost",
                        ["Email:ImapPort"] = "3143",
                        ["Email:ImapSecurityMode"] = "None",
                        ["Email:ImapUsername"] = SupportAddress,
                        ["Email:ImapPassword"] = "test",
                        ["Email:ImapAccountId"] = "greenmail-support",
                        ["Email:ImapFolder"] = "INBOX",
                        ["Email:SmtpHost"] = "localhost",
                        ["Email:SmtpPort"] = "3025",
                        ["Email:SmtpSecurityMode"] = "None",
                        ["Email:SupportMailboxAddress"] = SupportAddress,
                        ["Email:SupportMailboxDisplayName"] = "VS Help Desk"
                    }));
        });
    }

    private static async Task SendCustomerMessageAsync(string subject, string body, string fromAddress)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress("Customer", fromAddress));
        message.To.Add(new MailboxAddress("Support", SupportAddress));
        message.Subject = subject;
        message.Body = new TextPart("plain") { Text = body };

        using var client = new SmtpClient();
        await client.ConnectAsync("localhost", 3025, SecureSocketOptions.None);
        await client.SendAsync(message);
        await client.DisconnectAsync(true);
    }

    private static async Task<(uint UidValidity, uint Uid)> AssertOriginalMessageIsSeenAsync(string subject)
    {
        const int maxAttempts = 20;
        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            using var client = new ImapClient();
            await client.ConnectAsync("localhost", 3143, SecureSocketOptions.None);
            await client.AuthenticateAsync(SupportAddress, "test");
            var inbox = client.Inbox;
            await inbox.OpenAsync(FolderAccess.ReadOnly);

            var uids = await inbox.SearchAsync(SearchQuery.SubjectContains(subject));
            if (uids.Count > 0)
            {
                var summaries = await inbox.FetchAsync(
                    uids,
                    MessageSummaryItems.Flags | MessageSummaryItems.UniqueId);
                var match = Assert.Single(summaries);
                Assert.True(
                    match.Flags.HasValue && match.Flags.Value.HasFlag(MessageFlags.Seen),
                    "Original IMAP message should be marked Seen after the job.");
                var uidValidity = inbox.UidValidity;
                var uid = match.UniqueId.Id;
                await client.DisconnectAsync(true);
                return (uidValidity, uid);
            }

            await client.DisconnectAsync(true);
            await Task.Delay(TimeSpan.FromMilliseconds(250));
        }

        throw new TimeoutException(
            "GreenMail did not expose the processed message with the expected subject.");
    }

    private static async Task AssertUidStillSeenAsync(uint expectedUidValidity, uint uid)
    {
        using var client = new ImapClient();
        await client.ConnectAsync("localhost", 3143, SecureSocketOptions.None);
        await client.AuthenticateAsync(SupportAddress, "test");
        var inbox = client.Inbox;
        await inbox.OpenAsync(FolderAccess.ReadOnly);

        Assert.Equal(expectedUidValidity, inbox.UidValidity);
        var summaries = await inbox.FetchAsync(
            new[] { new UniqueId(uid) },
            MessageSummaryItems.Flags | MessageSummaryItems.UniqueId);
        var match = Assert.Single(summaries);
        Assert.True(
            match.Flags.HasValue && match.Flags.Value.HasFlag(MessageFlags.Seen),
            "Original IMAP UID must remain Seen so the job does not re-process the receipt.");

        var unseen = await inbox.SearchAsync(SearchQuery.NotSeen);
        Assert.DoesNotContain(new UniqueId(uid), unseen);

        await client.DisconnectAsync(true);
    }

    private static async Task ParkDueAcknowledgementsAsync(WebApplicationFactory<Program> factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var due = await db.ProcessedEmailMessages
            .Where(row =>
                (row.AcknowledgementStatus == AcknowledgementStatus.Pending
                 || row.AcknowledgementStatus == AcknowledgementStatus.Failed)
                && row.AcknowledgementNextAttemptAt != null
                && row.AcknowledgementNextAttemptAt <= DateTime.UtcNow)
            .ToListAsync();

        if (due.Count == 0)
        {
            return;
        }

        var parkUntil = DateTime.UtcNow.AddDays(1);
        foreach (var row in due)
        {
            db.Entry(row).Property(nameof(row.AcknowledgementNextAttemptAt)).CurrentValue = parkUntil;
            db.Entry(row).Property(nameof(row.AcknowledgementNextAttemptAt)).IsModified = true;
        }

        await db.SaveChangesAsync();
    }

    private static async Task CleanupCreatedAsync(
        WebApplicationFactory<Program> factory,
        Guid? ticketId,
        Guid? processedId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        if (processedId is Guid pid)
        {
            var processed = await db.ProcessedEmailMessages.FindAsync(pid);
            if (processed is not null)
            {
                db.ProcessedEmailMessages.Remove(processed);
            }
        }

        if (ticketId is Guid tid)
        {
            var messages = await db.TicketMessages.Where(row => row.TicketId == tid).ToListAsync();
            db.TicketMessages.RemoveRange(messages);

            var remainingProcessed = await db.ProcessedEmailMessages
                .Where(row => row.TicketId == tid)
                .ToListAsync();
            db.ProcessedEmailMessages.RemoveRange(remainingProcessed);

            var ticket = await db.Tickets.FindAsync(tid);
            if (ticket is not null)
            {
                db.Tickets.Remove(ticket);
            }
        }

        await db.SaveChangesAsync();
    }

    private sealed record JobPayload(
        int FetchedCount,
        int CreatedTickets,
        int AcknowledgementsSent,
        int AlreadyProcessed,
        int RetryableFailures,
        IReadOnlyList<string> CreatedTicketNumbers);
}
