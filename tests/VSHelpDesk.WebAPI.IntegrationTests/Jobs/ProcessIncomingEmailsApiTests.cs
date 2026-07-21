using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using VSHelpDesk.Application.Abstractions.Email;
using VSHelpDesk.Domain.Enums;
using VSHelpDesk.Infrastructure.Persistence;

using VSHelpDesk.WebAPI.IntegrationTests.Support;

namespace VSHelpDesk.WebAPI.IntegrationTests.Jobs;

public sealed class ProcessIncomingEmailsApiTests : IClassFixture<CustomWebApplicationFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly WebApplicationFactory<Program> baseFactory;

    public ProcessIncomingEmailsApiTests(CustomWebApplicationFactory factory)
    {
        baseFactory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("environment", "Development");
            builder.UseEnvironment("Development");
        });
    }

    [Fact]
    public async Task Jobs_ProcessIncomingEmails_HappyPath_CreatesExactTicketAndIdempotentReplay()
    {
        var token = Guid.NewGuid().ToString("N");
        var messageId = $"<exact-job-{token}@vshelpdesk.test>";
        var receiptValue = $"fake\0exact-job-{token}";
        var customerEmail = $"customer-{token[..8]}@example.test";
        var body = $"Exact job body {token}";

        var incoming = new IncomingEmail(
            MessageId: messageId,
            ReceiptHandle: new EmailReceiptHandle(EmailReceiptKind.Fake, receiptValue),
            FromAddress: customerEmail,
            FromDisplayName: "Exact Customer",
            Subject: $"Exact job subject {token}",
            Body: body,
            IsHtml: false,
            ReceivedAt: DateTime.UtcNow.AddMinutes(-2),
            Attachments: Array.Empty<IncomingEmailAttachment>());

        var receiver = new ControllableEmailReceiver(incoming);
        var sender = new RecordingEmailSender();
        await using var factory = CreateFactory(receiver, sender);
        var apiKey = GetJobsApiKey(factory);

        await ParkDueAcknowledgementsAsync(factory);

        Guid? ticketId = null;
        Guid? processedId = null;
        Guid? messageRowId = null;

        try
        {
            using var client = factory.CreateClient();
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/jobs/process-incoming-emails");
            request.Headers.Add("X-Jobs-Api-Key", apiKey);
            using var response = await client.SendAsync(request);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var payload = await response.Content.ReadFromJsonAsync<JobPayload>(JsonOptions);
            Assert.NotNull(payload);
            Assert.Equal(1, payload.FetchedCount);
            Assert.Equal(1, payload.CreatedTickets);
            Assert.Equal(1, payload.AcknowledgementsSent);
            Assert.Equal(0, payload.RetryableFailures);
            Assert.Equal(0, payload.Quarantined);
            Assert.Equal(0, payload.AlreadyProcessed);
            Assert.Single(payload.CreatedTicketNumbers);

            var ticketNumber = Assert.Single(payload.CreatedTicketNumbers);

            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var processed = await db.ProcessedEmailMessages
                    .SingleAsync(row => row.IdempotencyKey == messageId);
                processedId = processed.Id;
                Assert.Equal(ProcessedEmailDisposition.CreatedTicket, processed.Disposition);
                Assert.Equal(AcknowledgementStatus.Sent, processed.AcknowledgementStatus);
                Assert.True(processed.AcknowledgementAttempts >= 1);
                Assert.NotNull(processed.TicketId);
                ticketId = processed.TicketId;

                var ticket = await db.Tickets.SingleAsync(row => row.Id == ticketId);
                Assert.Equal(ticketNumber, ticket.TicketNumber);
                Assert.Equal(customerEmail, ticket.CustomerEmail);
                Assert.Equal($"Exact job subject {token}", ticket.Subject);

                var messages = await db.TicketMessages
                    .Where(row => row.TicketId == ticketId)
                    .OrderBy(row => row.CreatedAt)
                    .ToListAsync();
                var firstCustomer = Assert.Single(messages);
                messageRowId = firstCustomer.Id;
                Assert.Equal(MessageSenderType.Customer, firstCustomer.SenderType);
                Assert.Equal(body, firstCustomer.Content);
            }

            Assert.Single(sender.Sent);
            Assert.Equal(customerEmail, sender.Sent[0].ToAddress);
            Assert.Contains(ticketNumber, sender.Sent[0].Subject, StringComparison.Ordinal);
            Assert.Single(receiver.Marked);
            Assert.Equal(receiptValue, receiver.Marked[0].Value);

            // Re-expose the same receipt and prove DB idempotency (no second ticket/message/ack).
            receiver.Reexpose(incoming);
            sender.Sent.Clear();
            receiver.Marked.Clear();

            using var secondRequest = new HttpRequestMessage(HttpMethod.Post, "/api/jobs/process-incoming-emails");
            secondRequest.Headers.Add("X-Jobs-Api-Key", apiKey);
            using var secondResponse = await client.SendAsync(secondRequest);

            Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
            var secondPayload = await secondResponse.Content.ReadFromJsonAsync<JobPayload>(JsonOptions);
            Assert.NotNull(secondPayload);
            Assert.Equal(1, secondPayload.FetchedCount);
            Assert.Equal(0, secondPayload.CreatedTickets);
            Assert.Equal(1, secondPayload.AlreadyProcessed);
            Assert.Equal(0, secondPayload.AcknowledgementsSent);
            Assert.Empty(secondPayload.CreatedTicketNumbers);
            Assert.Empty(sender.Sent);

            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                Assert.Equal(
                    1,
                    await db.ProcessedEmailMessages.CountAsync(row => row.IdempotencyKey == messageId));
                Assert.Equal(
                    1,
                    await db.Tickets.CountAsync(row => row.Id == ticketId));
                Assert.Equal(
                    1,
                    await db.TicketMessages.CountAsync(row => row.TicketId == ticketId));
            }
        }
        finally
        {
            await CleanupCreatedAsync(factory, ticketId, processedId, messageRowId);
        }
    }

    [Fact]
    public async Task ProcessIncomingEmails_WithAttachment_StoresOnTicketAndDownloadWorks()
    {
        var token = Guid.NewGuid().ToString("N");
        var messageId = $"<attach-job-{token}@vshelpdesk.test>";
        var receiptValue = $"fake\0attach-job-{token}";
        var customerEmail = $"attach-{token[..8]}@example.test";
        var attachmentBytes = Encoding.UTF8.GetBytes($"fake-attachment-{token}");
        var storageRoot = Path.Combine(
            Path.GetTempPath(),
            "vshd-it-inbound-storage",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(storageRoot);

        var incoming = new IncomingEmail(
            MessageId: messageId,
            ReceiptHandle: new EmailReceiptHandle(EmailReceiptKind.Fake, receiptValue),
            FromAddress: customerEmail,
            FromDisplayName: "Attach Customer",
            Subject: $"Attach job subject {token}",
            Body: $"Attach job body {token}",
            IsHtml: false,
            ReceivedAt: DateTime.UtcNow.AddMinutes(-2),
            Attachments:
            [
                new IncomingEmailAttachment(
                    FileName: "note.txt",
                    ContentType: "text/plain",
                    FileSize: attachmentBytes.Length,
                    Content: attachmentBytes)
            ]);

        var receiver = new ControllableEmailReceiver(incoming);
        var sender = new RecordingEmailSender();
        await using var factory = CreateFactory(receiver, sender, storageRoot);
        var apiKey = GetJobsApiKey(factory);
        await ParkDueAcknowledgementsAsync(factory);

        Guid? ticketId = null;
        Guid? processedId = null;
        Guid? messageRowId = null;
        Guid? attachmentId = null;

        try
        {
            using var jobsClient = factory.CreateClient();
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/jobs/process-incoming-emails");
            request.Headers.Add("X-Jobs-Api-Key", apiKey);
            using var response = await jobsClient.SendAsync(request);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var payload = await response.Content.ReadFromJsonAsync<JobPayload>(JsonOptions);
            Assert.NotNull(payload);
            Assert.Equal(1, payload.CreatedTickets);

            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var processed = await db.ProcessedEmailMessages
                    .SingleAsync(row => row.IdempotencyKey == messageId);
                processedId = processed.Id;
                ticketId = processed.TicketId;
                Assert.NotNull(ticketId);

                var messages = await db.TicketMessages
                    .Where(row => row.TicketId == ticketId)
                    .ToListAsync();
                var firstCustomer = Assert.Single(messages);
                messageRowId = firstCustomer.Id;

                var attachment = await db.TicketAttachments
                    .SingleAsync(row => row.TicketMessageId == firstCustomer.Id);
                attachmentId = attachment.Id;
                Assert.Equal("note.txt", attachment.FileName);
                Assert.Equal("text/plain", attachment.ContentType);
                Assert.Equal(attachmentBytes.Length, attachment.FileSize);
                Assert.True(File.Exists(attachment.FilePath));
            }

            var (supportClient, _, _) = await CookieAuthTestHelper.LoginAsSupportAsync(factory);
            using (supportClient)
            {
                using var detailResponse = await supportClient.GetAsync($"/api/tickets/{ticketId}");
                Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
                using var detailDoc = JsonDocument.Parse(await detailResponse.Content.ReadAsStringAsync());
                var attachments = detailDoc.RootElement.GetProperty("attachments");
                Assert.Equal(1, attachments.GetArrayLength());
                Assert.Equal(attachmentId, attachments[0].GetProperty("id").GetGuid());
                Assert.Equal("note.txt", attachments[0].GetProperty("fileName").GetString());

                using var downloadRequest = new HttpRequestMessage(
                    HttpMethod.Get,
                    $"/api/attachments/{attachmentId}");
                using var downloadResponse = await supportClient.SendAsync(downloadRequest);
                Assert.Equal(HttpStatusCode.OK, downloadResponse.StatusCode);
                Assert.Equal(
                    Encoding.UTF8.GetString(attachmentBytes),
                    await downloadResponse.Content.ReadAsStringAsync());
            }
        }
        finally
        {
            await CleanupCreatedAsync(factory, ticketId, processedId, messageRowId, attachmentId);
            if (Directory.Exists(storageRoot))
            {
                Directory.Delete(storageRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task JobApi_UsesQuarantineAndRetryableFailureCounters()
    {
        var token = Guid.NewGuid().ToString("N");
        var incoming = new IncomingEmail(
            MessageId: $"<counters-{token}@vshelpdesk.test>",
            ReceiptHandle: new EmailReceiptHandle(EmailReceiptKind.Fake, $"fake\0counters-{token}"),
            FromAddress: $"counters-{token[..8]}@example.test",
            FromDisplayName: "Counters Customer",
            Subject: $"Counters subject {token}",
            Body: $"Counters body {token}",
            IsHtml: false,
            ReceivedAt: DateTime.UtcNow.AddMinutes(-1),
            Attachments: Array.Empty<IncomingEmailAttachment>());

        var receiver = new ControllableEmailReceiver(incoming);
        var sender = new RecordingEmailSender();
        await using var factory = CreateFactory(receiver, sender);
        var apiKey = GetJobsApiKey(factory);
        await ParkDueAcknowledgementsAsync(factory);

        Guid? ticketId = null;
        Guid? processedId = null;
        Guid? messageRowId = null;

        try
        {
            using var client = factory.CreateClient();
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/jobs/process-incoming-emails");
            request.Headers.Add("X-Jobs-Api-Key", apiKey);
            using var response = await client.SendAsync(request);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var root = doc.RootElement;

            Assert.Equal(0, root.GetProperty("quarantined").GetInt32());
            Assert.Equal(0, root.GetProperty("retryableFailures").GetInt32());
            Assert.True(root.TryGetProperty("failures", out var failures));
            Assert.Equal(JsonValueKind.Array, failures.ValueKind);
            Assert.Equal(0, failures.GetArrayLength());
            Assert.False(root.TryGetProperty("skippedInvalid", out _));
            Assert.False(root.TryGetProperty("messageIds", out _));
            Assert.True(root.TryGetProperty("acknowledgementsSent", out _));
            Assert.True(root.TryGetProperty("acknowledgementsFailed", out _));
            Assert.True(root.TryGetProperty("createdTicketNumbers", out _));

            var messageId = incoming.MessageId!;
            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var processed = await db.ProcessedEmailMessages
                .SingleAsync(row => row.IdempotencyKey == messageId);
            processedId = processed.Id;
            ticketId = processed.TicketId;
            if (ticketId is not null)
            {
                messageRowId = await db.TicketMessages
                    .Where(row => row.TicketId == ticketId)
                    .Select(row => (Guid?)row.Id)
                    .FirstOrDefaultAsync();
            }
        }
        finally
        {
            await CleanupCreatedAsync(factory, ticketId, processedId, messageRowId);
        }
    }

    private WebApplicationFactory<Program> CreateFactory(
        ControllableEmailReceiver receiver,
        RecordingEmailSender sender,
        string? storageRoot = null) =>
        baseFactory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("environment", "Development");
            builder.UseEnvironment("Development");
            if (storageRoot is not null)
            {
                builder.ConfigureAppConfiguration((_, config) =>
                {
                    config.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["FileStorage:RootPath"] = storageRoot,
                        ["FileStorage:MaxFileSizeBytes"] = "1048576",
                        ["FileStorage:AllowedContentTypes:0"] = "text/plain",
                        ["FileStorage:AllowedContentTypes:1"] = "application/pdf"
                    });
                });
            }

            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IEmailReceiver>();
                services.AddSingleton<IEmailReceiver>(receiver);

                services.RemoveAll<IEmailSender>();
                services.AddSingleton<IEmailSender>(sender);
            });
        });

    private static string GetJobsApiKey(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var apiKey = configuration["Jobs:ApiKey"];
        Assert.False(string.IsNullOrWhiteSpace(apiKey));
        return apiKey!;
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
        Guid? processedId,
        Guid? messageRowId,
        Guid? attachmentId = null)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        if (attachmentId is Guid aid)
        {
            var attachment = await db.TicketAttachments.FindAsync(aid);
            if (attachment is not null)
            {
                db.TicketAttachments.Remove(attachment);
            }
        }

        if (processedId is Guid pid)
        {
            var processed = await db.ProcessedEmailMessages.FindAsync(pid);
            if (processed is not null)
            {
                db.ProcessedEmailMessages.Remove(processed);
            }
        }

        if (messageRowId is Guid mid)
        {
            var remainingAttachments = await db.TicketAttachments
                .Where(row => row.TicketMessageId == mid)
                .ToListAsync();
            db.TicketAttachments.RemoveRange(remainingAttachments);

            var message = await db.TicketMessages.FindAsync(mid);
            if (message is not null)
            {
                db.TicketMessages.Remove(message);
            }
        }

        if (ticketId is Guid tid)
        {
            var remainingMessages = await db.TicketMessages
                .Where(row => row.TicketId == tid)
                .ToListAsync();
            var remainingMessageIds = remainingMessages.Select(m => m.Id).ToList();
            if (remainingMessageIds.Count > 0)
            {
                var remainingAttachments = await db.TicketAttachments
                    .Where(row => remainingMessageIds.Contains(row.TicketMessageId))
                    .ToListAsync();
                db.TicketAttachments.RemoveRange(remainingAttachments);
            }

            db.TicketMessages.RemoveRange(remainingMessages);

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

    private sealed class ControllableEmailReceiver(IncomingEmail initial) : IEmailReceiver
    {
        private IncomingEmail? pending = initial;

        public List<EmailReceiptHandle> Marked { get; } = [];

        public void Reexpose(IncomingEmail email) => pending = email;

        public Task<IReadOnlyList<IncomingEmail>> FetchUnreadAsync(
            CancellationToken cancellationToken = default)
        {
            IReadOnlyList<IncomingEmail> batch = pending is null ? [] : [pending];
            return Task.FromResult(batch);
        }

        public Task MarkAsProcessedAsync(
            EmailReceiptHandle receiptHandle,
            CancellationToken cancellationToken = default)
        {
            Marked.Add(receiptHandle);
            if (pending is not null
                && string.Equals(pending.ReceiptHandle.Value, receiptHandle.Value, StringComparison.Ordinal))
            {
                pending = null;
            }

            return Task.CompletedTask;
        }
    }

    private sealed class RecordingEmailSender : IEmailSender
    {
        public List<EmailMessage> Sent { get; } = [];

        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
        {
            Sent.Add(message);
            return Task.CompletedTask;
        }
    }

    private sealed record JobPayload(
        int FetchedCount,
        int CreatedTickets,
        int AcknowledgementsSent,
        int AlreadyProcessed,
        int Quarantined,
        int RetryableFailures,
        IReadOnlyList<string> CreatedTicketNumbers);
}
