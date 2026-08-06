using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using VSHelpDesk.Application.Abstractions.Email;
using VSHelpDesk.Application.Features.Parameters;
using VSHelpDesk.Domain.Entities;
using VSHelpDesk.Domain.Enums;
using VSHelpDesk.Domain.Tickets;
using VSHelpDesk.Infrastructure.Persistence;

using VSHelpDesk.WebAPI.IntegrationTests.Support;

namespace VSHelpDesk.WebAPI.IntegrationTests.Tickets;

/// <summary>
/// End-to-end lifecycle proof on the real PostgreSQL host:
/// manual/automatic resolve → reply guard → customer-email reopen → idempotency.
/// </summary>
public sealed class TicketLifecycleApiTests : IClassFixture<CustomWebApplicationFactory>
{
    private static readonly DateTimeOffset FixedNow =
        new(2026, 8, 11, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTime CutoffUtc = FixedNow.UtcDateTime.AddDays(-3);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly WebApplicationFactory<Program> baseFactory;

    public TicketLifecycleApiTests(CustomWebApplicationFactory factory)
    {
        baseFactory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("environment", "Development");
            builder.UseEnvironment("Development");
        });
    }

    [Fact]
    public async Task ManualResolve_ReplyGuard_CustomerMailReopensSameTicketIdempotently()
    {
        var token = Guid.NewGuid().ToString("N");
        var ticketNumber = UniqueCanonicalTicketNumber();
        var customerEmail = $"lifecycle-manual-{token[..8]}@example.test";
        const string subject = "Lifecycle manual subject";
        const string reopenBody = "Customer reopen body for lifecycle manual path.";
        var reopenMessageId = $"<lifecycle-manual-reopen-{token}@vshelpdesk.test>";
        var reopenReceipt = $"fake\0lifecycle-manual-reopen-{token}";

        var receiver = new ControllableEmailReceiver();
        var sender = new RecordingEmailSender();
        await using var factory = CreateFactory(receiver, sender);
        var jobsApiKey = GetJobsApiKey(factory);

        var (client, csrf, loginUserId) = await CookieAuthTestHelper.LoginAsSupportAsync(factory);
        using var portalClient = client;
        await ParkDueAcknowledgementsAsync(factory);

        Guid ticketId = default;
        var createdTicketIds = new List<Guid>();
        var initialMessageCount = 0;

        try
        {
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var stamp = DateTime.UtcNow.AddHours(-2);
                var ticket = Ticket.Create(
                    ticketNumber,
                    subject,
                    "Lifecycle Customer",
                    customerEmail,
                    stamp);
                ticket.MarkAsCustomerReplied(stamp.AddMinutes(5));
                var seedMessage = new TicketMessage(
                    ticket.Id,
                    MessageSenderType.Customer,
                    "Initial customer message before resolve.",
                    isHtml: false,
                    userId: null,
                    createdAtUtc: stamp.AddMinutes(5));
                db.Add(ticket);
                db.Add(seedMessage);
                await db.SaveChangesAsync();
                ticketId = ticket.Id;
                createdTicketIds.Add(ticketId);
                initialMessageCount = 1;
            }

            // 3–4. Manual resolve
            using (var resolveRequest = new HttpRequestMessage(
                       HttpMethod.Post,
                       $"/api/tickets/{ticketId}/resolve"))
            {
                CookieAuthTestHelper.AddCsrf(resolveRequest, csrf);
                using var resolveResponse = await client.SendAsync(resolveRequest);
                Assert.Equal(HttpStatusCode.OK, resolveResponse.StatusCode);
                using var resolveDoc = JsonDocument.Parse(
                    await resolveResponse.Content.ReadAsStringAsync());
                var resolveRoot = resolveDoc.RootElement;
                Assert.True(resolveRoot.GetProperty("changed").GetBoolean());
                Assert.Equal("Resolved", resolveRoot.GetProperty("status").GetString());
                Assert.Equal(loginUserId, resolveRoot.GetProperty("closedByUserId").GetGuid());
                Assert.NotEqual(
                    default,
                    resolveRoot.GetProperty("resolvedAt").GetDateTime());
            }

            // 5. Support reply guard
            sender.Sent.Clear();
            using (var replyRequest = new HttpRequestMessage(
                HttpMethod.Post,
                $"/api/tickets/{ticketId}/replies")
            {
                Content = JsonContent.Create(new
                {
                    content = "Should never persist on resolved lifecycle ticket."
                })
            })
            {
                CookieAuthTestHelper.AddCsrf(replyRequest, csrf);
                using var replyResponse = await client.SendAsync(replyRequest);
                Assert.Equal(HttpStatusCode.Conflict, replyResponse.StatusCode);
            }

            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                Assert.Equal(
                    initialMessageCount,
                    await db.TicketMessages.CountAsync(m => m.TicketId == ticketId));
            }

            Assert.Empty(sender.Sent);

            // 6–8. Matching customer mail reopens same ticket
            var incoming = new IncomingEmail(
                MessageId: reopenMessageId,
                ReceiptHandle: new EmailReceiptHandle(EmailReceiptKind.Fake, reopenReceipt),
                FromAddress: customerEmail,
                FromDisplayName: "Lifecycle Customer",
                Subject: $"Re: [{ticketNumber}] {subject} — reopen suffix {token[..6]}",
                Body: reopenBody,
                IsHtml: false,
                ReceivedAt: DateTime.UtcNow.AddMinutes(-1),
                Attachments: Array.Empty<IncomingEmailAttachment>(),
                AuthenticationVerdict: new EmailAuthenticationVerdict(true, true, null, "dmarc=pass"));
            receiver.Expose(incoming);

            using (var jobRequest = new HttpRequestMessage(
                       HttpMethod.Post,
                       "/api/jobs/process-incoming-emails"))
            {
                jobRequest.Headers.Add("X-Jobs-Api-Key", jobsApiKey);
                using var jobResponse = await client.SendAsync(jobRequest);
                Assert.Equal(HttpStatusCode.OK, jobResponse.StatusCode);
                var jobPayload = await jobResponse.Content.ReadFromJsonAsync<MailJobPayload>(JsonOptions);
                Assert.NotNull(jobPayload);
                Assert.Equal(1, jobPayload.FetchedCount);
                Assert.Equal(1, jobPayload.CustomerReplies);
                Assert.Equal(1, jobPayload.ReopenedTickets);
                Assert.Equal(0, jobPayload.CreatedTickets);
                Assert.Equal(0, jobPayload.AlreadyProcessed);
                Assert.Empty(jobPayload.CreatedTicketNumbers);
            }

            // 9. DB + authenticated detail
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var ticket = await db.Tickets.SingleAsync(t => t.Id == ticketId);
                Assert.Equal(ticketNumber, ticket.TicketNumber);
                Assert.Equal(subject, ticket.Subject);
                Assert.Equal(TicketStatus.CustomerReplied, ticket.Status);
                Assert.Null(ticket.ResolvedAt);
                Assert.Null(ticket.ClosedByUserId);

                var messages = await db.TicketMessages
                    .Where(m => m.TicketId == ticketId)
                    .OrderBy(m => m.CreatedAt)
                    .ThenBy(m => m.Id)
                    .ToListAsync();
                Assert.Equal(initialMessageCount + 1, messages.Count);
                Assert.Equal(MessageSenderType.Customer, messages[^1].SenderType);
                Assert.Equal(reopenBody, messages[^1].Content);
                for (var i = 1; i < messages.Count; i++)
                {
                    Assert.True(
                        messages[i - 1].CreatedAt <= messages[i].CreatedAt,
                        "Messages must be chronological.");
                }
            }

            using (var detailRequest = new HttpRequestMessage(
                       HttpMethod.Get,
                       $"/api/tickets/{ticketId}"))
            {
                using var detailResponse = await client.SendAsync(detailRequest);
                Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
                using var detailDoc = JsonDocument.Parse(
                    await detailResponse.Content.ReadAsStringAsync());
                var detail = detailDoc.RootElement;
                Assert.Equal(ticketId, detail.GetProperty("id").GetGuid());
                Assert.Equal(ticketNumber, detail.GetProperty("ticketNumber").GetString());
                Assert.Equal(subject, detail.GetProperty("subject").GetString());
                Assert.Equal("CustomerReplied", detail.GetProperty("status").GetString());
                Assert.Equal(JsonValueKind.Null, detail.GetProperty("resolvedAt").ValueKind);
                Assert.Equal(JsonValueKind.Null, detail.GetProperty("closedByUserId").ValueKind);
                var detailMessages = detail.GetProperty("messages");
                Assert.Equal(initialMessageCount + 1, detailMessages.GetArrayLength());
                Assert.Equal(
                    reopenBody,
                    detailMessages[detailMessages.GetArrayLength() - 1]
                        .GetProperty("content")
                        .GetString());
            }

            // 10–11. Idempotent re-process of same email identity
            receiver.Reexpose(incoming);
            using (var secondJobRequest = new HttpRequestMessage(
                       HttpMethod.Post,
                       "/api/jobs/process-incoming-emails"))
            {
                secondJobRequest.Headers.Add("X-Jobs-Api-Key", jobsApiKey);
                using var secondJobResponse = await client.SendAsync(secondJobRequest);
                Assert.Equal(HttpStatusCode.OK, secondJobResponse.StatusCode);
                var secondPayload =
                    await secondJobResponse.Content.ReadFromJsonAsync<MailJobPayload>(JsonOptions);
                Assert.NotNull(secondPayload);
                Assert.Equal(1, secondPayload.FetchedCount);
                Assert.Equal(1, secondPayload.AlreadyProcessed);
                Assert.Equal(0, secondPayload.CreatedTickets);
                Assert.Equal(0, secondPayload.CustomerReplies);
                Assert.Equal(0, secondPayload.ReopenedTickets);
            }

            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                Assert.Equal(
                    1,
                    await db.Tickets.CountAsync(t => t.TicketNumber == ticketNumber));
                Assert.Equal(
                    initialMessageCount + 1,
                    await db.TicketMessages.CountAsync(m => m.TicketId == ticketId));
                var ticket = await db.Tickets.SingleAsync(t => t.Id == ticketId);
                Assert.Equal(TicketStatus.CustomerReplied, ticket.Status);
                Assert.Null(ticket.ResolvedAt);
                Assert.Null(ticket.ClosedByUserId);
            }

            using (var detailRequest = new HttpRequestMessage(
                       HttpMethod.Get,
                       $"/api/tickets/{ticketId}"))
            {
                using var detailResponse = await client.SendAsync(detailRequest);
                Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
                using var detailDoc = JsonDocument.Parse(
                    await detailResponse.Content.ReadAsStringAsync());
                var detail = detailDoc.RootElement;
                Assert.Equal(ticketId, detail.GetProperty("id").GetGuid());
                Assert.Equal("CustomerReplied", detail.GetProperty("status").GetString());
                Assert.Equal(initialMessageCount + 1, detail.GetProperty("messages").GetArrayLength());
            }
        }
        finally
        {
            await CleanupTicketsAsync(factory, createdTicketIds);
        }
    }

    [Fact]
    public async Task AutomaticResolve_ThenMatchingCustomerMail_ReopensWithNullCloserHistory()
    {
        var token = Guid.NewGuid().ToString("N");
        var ticketNumber = UniqueCanonicalTicketNumber();
        var customerEmail = $"lifecycle-auto-{token[..8]}@example.test";
        const string subject = "Lifecycle automatic subject";
        const string reopenBody = "Customer reopen after automatic resolve.";
        var reopenMessageId = $"<lifecycle-auto-reopen-{token}@vshelpdesk.test>";
        var reopenReceipt = $"fake\0lifecycle-auto-reopen-{token}";

        var receiver = new ControllableEmailReceiver();
        var sender = new RecordingEmailSender();
        await using var factory = CreateFactoryWithFixedTime(receiver, sender);
        await EnsureDefaultInactiveDaysAsync(factory);
        var jobsApiKey = GetJobsApiKey(factory);
        await ParkDueAcknowledgementsAsync(factory);

        var ticketIds = new List<Guid>();
        var parked = new List<ParkedTicket>();
        Guid ticketId = default;

        try
        {
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var ticket = Ticket.Create(
                    ticketNumber,
                    subject,
                    "Auto Customer",
                    customerEmail,
                    CutoffUtc.AddDays(-1));
                ticket.MarkAsWaitingCustomerReply(CutoffUtc);
                db.Add(ticket);
                await db.SaveChangesAsync();
                ticketId = ticket.Id;
                ticketIds.Add(ticketId);
                parked = await ParkForeignEligibleAsync(db, CutoffUtc, ticketIds);
            }

            using var client = factory.CreateClient();

            using (var resolveJobRequest = new HttpRequestMessage(
                       HttpMethod.Post,
                       "/api/jobs/resolve-inactive-tickets"))
            {
                resolveJobRequest.Headers.Add("X-Jobs-Api-Key", jobsApiKey);
                using var resolveJobResponse = await client.SendAsync(resolveJobRequest);
                Assert.Equal(HttpStatusCode.OK, resolveJobResponse.StatusCode);
                var resolvePayload =
                    await resolveJobResponse.Content.ReadFromJsonAsync<ResolveJobPayload>(JsonOptions);
                Assert.NotNull(resolvePayload);
                Assert.Equal(1, resolvePayload.Candidates);
                Assert.Equal(1, resolvePayload.Resolved);
            }

            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var ticket = await db.Tickets.SingleAsync(t => t.Id == ticketId);
                Assert.Equal(TicketStatus.Resolved, ticket.Status);
                Assert.Null(ticket.ClosedByUserId);
                Assert.NotNull(ticket.ResolvedAt);
                AssertEqualUtc(FixedNow.UtcDateTime, ticket.ResolvedAt!.Value);
            }

            var incoming = new IncomingEmail(
                MessageId: reopenMessageId,
                ReceiptHandle: new EmailReceiptHandle(EmailReceiptKind.Fake, reopenReceipt),
                FromAddress: customerEmail,
                FromDisplayName: "Auto Customer",
                Subject: $"Re: [{ticketNumber}] {subject} — auto reopen",
                Body: reopenBody,
                IsHtml: false,
                ReceivedAt: DateTime.UtcNow.AddMinutes(-1),
                Attachments: Array.Empty<IncomingEmailAttachment>(),
                AuthenticationVerdict: new EmailAuthenticationVerdict(true, true, null, "dmarc=pass"));
            receiver.Expose(incoming);

            using (var mailJobRequest = new HttpRequestMessage(
                       HttpMethod.Post,
                       "/api/jobs/process-incoming-emails"))
            {
                mailJobRequest.Headers.Add("X-Jobs-Api-Key", jobsApiKey);
                using var mailJobResponse = await client.SendAsync(mailJobRequest);
                Assert.Equal(HttpStatusCode.OK, mailJobResponse.StatusCode);
                var mailPayload =
                    await mailJobResponse.Content.ReadFromJsonAsync<MailJobPayload>(JsonOptions);
                Assert.NotNull(mailPayload);
                Assert.Equal(1, mailPayload.CustomerReplies);
                Assert.Equal(1, mailPayload.ReopenedTickets);
                Assert.Equal(0, mailPayload.CreatedTickets);
            }

            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                Assert.Equal(1, await db.Tickets.CountAsync(t => t.TicketNumber == ticketNumber));
                var ticket = await db.Tickets.SingleAsync(t => t.Id == ticketId);
                Assert.Equal(ticketNumber, ticket.TicketNumber);
                Assert.Equal(subject, ticket.Subject);
                Assert.Equal(TicketStatus.CustomerReplied, ticket.Status);
                Assert.Null(ticket.ResolvedAt);
                Assert.Null(ticket.ClosedByUserId);

                var messages = await db.TicketMessages
                    .Where(m => m.TicketId == ticketId)
                    .OrderBy(m => m.CreatedAt)
                    .ToListAsync();
                Assert.Single(messages);
                Assert.Equal(MessageSenderType.Customer, messages[0].SenderType);
                Assert.Equal(reopenBody, messages[0].Content);
            }
        }
        finally
        {
            await CleanupTicketsAsync(factory, ticketIds);
            await RestoreParkedAsync(factory, parked);
        }
    }

    [Fact]
    public async Task ResolvedTicket_MismatchedCustomerAddress_CreatesNewTicketWithoutReopeningOriginal()
    {
        var token = Guid.NewGuid().ToString("N");
        var ticketNumber = UniqueCanonicalTicketNumber();
        var ownerEmail = $"lifecycle-owner-{token[..8]}@example.test";
        var spoofEmail = $"lifecycle-spoof-{token[..8]}@evil.test";
        const string subject = "Owned by lifecycle owner";
        var messageId = $"<lifecycle-spoof-{token}@vshelpdesk.test>";
        var receipt = $"fake\0lifecycle-spoof-{token}";

        var receiver = new ControllableEmailReceiver();
        var sender = new RecordingEmailSender();
        await using var factory = CreateFactory(receiver, sender);
        var jobsApiKey = GetJobsApiKey(factory);
        var (client, _, loginUserId) = await CookieAuthTestHelper.LoginAsSupportAsync(factory);
        using var portalClient = client;
        await ParkDueAcknowledgementsAsync(factory);

        Guid originalTicketId = default;
        Guid? newTicketId = null;
        var ticketIds = new List<Guid>();

        try
        {
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var stamp = DateTime.UtcNow.AddHours(-3);
                var ticket = Ticket.Create(
                    ticketNumber,
                    subject,
                    "Owner",
                    ownerEmail,
                    stamp);
                Assert.True(ticket.ResolveManually(stamp.AddHours(1), loginUserId));
                db.Add(ticket);
                await db.SaveChangesAsync();
                originalTicketId = ticket.Id;
                ticketIds.Add(originalTicketId);
            }

            var incoming = new IncomingEmail(
                MessageId: messageId,
                ReceiptHandle: new EmailReceiptHandle(EmailReceiptKind.Fake, receipt),
                FromAddress: spoofEmail,
                FromDisplayName: "Spoof",
                Subject: $"Re: [{ticketNumber}] {subject}",
                Body: "Inject attempt body",
                IsHtml: false,
                ReceivedAt: DateTime.UtcNow.AddMinutes(-1),
                Attachments: Array.Empty<IncomingEmailAttachment>(),
                AuthenticationVerdict: new EmailAuthenticationVerdict(true, true, null, "dmarc=pass"));
            receiver.Expose(incoming);

            using (var jobRequest = new HttpRequestMessage(
                       HttpMethod.Post,
                       "/api/jobs/process-incoming-emails"))
            {
                jobRequest.Headers.Add("X-Jobs-Api-Key", jobsApiKey);
                using var jobResponse = await client.SendAsync(jobRequest);
                Assert.Equal(HttpStatusCode.OK, jobResponse.StatusCode);
                var payload = await jobResponse.Content.ReadFromJsonAsync<MailJobPayload>(JsonOptions);
                Assert.NotNull(payload);
                Assert.Equal(1, payload.CreatedTickets);
                Assert.Equal(0, payload.CustomerReplies);
                Assert.Equal(0, payload.ReopenedTickets);
                Assert.Single(payload.CreatedTicketNumbers);
            }

            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var original = await db.Tickets.SingleAsync(t => t.Id == originalTicketId);
                Assert.Equal(TicketStatus.Resolved, original.Status);
                Assert.Equal(loginUserId, original.ClosedByUserId);
                Assert.NotNull(original.ResolvedAt);
                Assert.Equal(0, await db.TicketMessages.CountAsync(m => m.TicketId == originalTicketId));

                var created = await db.Tickets
                    .Where(t => t.CustomerEmail == spoofEmail)
                    .SingleAsync();
                newTicketId = created.Id;
                ticketIds.Add(created.Id);
                Assert.NotEqual(originalTicketId, created.Id);
                Assert.NotEqual(ticketNumber, created.TicketNumber);
                Assert.Equal(TicketStatus.New, created.Status);
            }

            // Touch detail for original to confirm still resolved via API.
            using var detailRequest = new HttpRequestMessage(
                HttpMethod.Get,
                $"/api/tickets/{originalTicketId}");
            using var detailResponse = await client.SendAsync(detailRequest);
            Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
            using var detailDoc = JsonDocument.Parse(await detailResponse.Content.ReadAsStringAsync());
            Assert.Equal("Resolved", detailDoc.RootElement.GetProperty("status").GetString());
            Assert.Equal(
                loginUserId,
                detailDoc.RootElement.GetProperty("closedByUserId").GetGuid());
        }
        finally
        {
            await CleanupTicketsAsync(factory, ticketIds);
            _ = newTicketId;
        }
    }

    [Fact]
    public async Task ReopenedTicket_AppearsInListAndDetailAsCustomerReplied()
    {
        var token = Guid.NewGuid().ToString("N");
        var ticketNumber = UniqueCanonicalTicketNumber();
        var customerEmail = $"lifecycle-list-{token[..8]}@example.test";
        const string subject = "Lifecycle list/detail subject";
        const string reopenBody = "Reopened list visibility body.";
        var reopenMessageId = $"<lifecycle-list-reopen-{token}@vshelpdesk.test>";
        var reopenReceipt = $"fake\0lifecycle-list-reopen-{token}";

        var receiver = new ControllableEmailReceiver();
        var sender = new RecordingEmailSender();
        await using var factory = CreateFactory(receiver, sender);
        var jobsApiKey = GetJobsApiKey(factory);
        var (client, csrf, loginUserId) = await CookieAuthTestHelper.LoginAsSupportAsync(factory);
        using var portalClient = client;
        await ParkDueAcknowledgementsAsync(factory);

        Guid ticketId = default;
        var ticketIds = new List<Guid>();

        try
        {
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var stamp = DateTime.UtcNow.AddHours(-4);
                var ticket = Ticket.Create(
                    ticketNumber,
                    subject,
                    "List Customer",
                    customerEmail,
                    stamp);
                ticket.MarkAsCustomerReplied(stamp.AddMinutes(10));
                var seedMessage = new TicketMessage(
                    ticket.Id,
                    MessageSenderType.Customer,
                    "Seed before list lifecycle.",
                    isHtml: false,
                    userId: null,
                    createdAtUtc: stamp.AddMinutes(10));
                db.Add(ticket);
                db.Add(seedMessage);
                await db.SaveChangesAsync();
                ticketId = ticket.Id;
                ticketIds.Add(ticketId);
            }

            using (var resolveRequest = new HttpRequestMessage(
                       HttpMethod.Post,
                       $"/api/tickets/{ticketId}/resolve"))
            {
                CookieAuthTestHelper.AddCsrf(resolveRequest, csrf);
                using var resolveResponse = await client.SendAsync(resolveRequest);
                Assert.Equal(HttpStatusCode.OK, resolveResponse.StatusCode);
            }

            var incoming = new IncomingEmail(
                MessageId: reopenMessageId,
                ReceiptHandle: new EmailReceiptHandle(EmailReceiptKind.Fake, reopenReceipt),
                FromAddress: customerEmail,
                FromDisplayName: "List Customer",
                Subject: $"Re: [{ticketNumber}] {subject}",
                Body: reopenBody,
                IsHtml: false,
                ReceivedAt: DateTime.UtcNow.AddMinutes(-1),
                Attachments: Array.Empty<IncomingEmailAttachment>(),
                AuthenticationVerdict: new EmailAuthenticationVerdict(true, true, null, "dmarc=pass"));
            receiver.Expose(incoming);

            using (var jobRequest = new HttpRequestMessage(
                       HttpMethod.Post,
                       "/api/jobs/process-incoming-emails"))
            {
                jobRequest.Headers.Add("X-Jobs-Api-Key", jobsApiKey);
                using var jobResponse = await client.SendAsync(jobRequest);
                Assert.Equal(HttpStatusCode.OK, jobResponse.StatusCode);
                var payload = await jobResponse.Content.ReadFromJsonAsync<MailJobPayload>(JsonOptions);
                Assert.NotNull(payload);
                Assert.Equal(1, payload.CustomerReplies);
                Assert.Equal(1, payload.ReopenedTickets);
                Assert.Equal(0, payload.CreatedTickets);
            }

            DateTime expectedLastActivity;
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var ticket = await db.Tickets.SingleAsync(t => t.Id == ticketId);
                Assert.Equal(TicketStatus.CustomerReplied, ticket.Status);
                Assert.Null(ticket.ResolvedAt);
                Assert.Null(ticket.ClosedByUserId);
                expectedLastActivity = ticket.LastActivityAt;
            }

            using (var listRequest = new HttpRequestMessage(HttpMethod.Get, "/api/tickets"))
            {
                using var listResponse = await client.SendAsync(listRequest);
                Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
                using var listDoc = JsonDocument.Parse(await listResponse.Content.ReadAsStringAsync());
                Assert.Equal(JsonValueKind.Object, listDoc.RootElement.ValueKind);
                var listItems = listDoc.RootElement.GetProperty("items");

                JsonElement? match = null;
                var index = 0;
                var matchIndex = -1;
                foreach (var item in listItems.EnumerateArray())
                {
                    if (item.GetProperty("id").GetGuid() == ticketId)
                    {
                        match = item;
                        matchIndex = index;
                        break;
                    }

                    index++;
                }

                Assert.True(match.HasValue, "Reopened ticket must appear in list.");
                var listItem = match.Value;
                Assert.Equal(ticketNumber, listItem.GetProperty("ticketNumber").GetString());
                Assert.Equal("CustomerReplied", listItem.GetProperty("status").GetString());
                Assert.Equal(
                    DateTime.SpecifyKind(expectedLastActivity, DateTimeKind.Utc),
                    DateTime.SpecifyKind(
                        listItem.GetProperty("lastActivityAt").GetDateTime(),
                        DateTimeKind.Utc),
                    TimeSpan.FromMilliseconds(1));

                // Newest LastActivityAt first: any preceding row must be >= this ticket's activity.
                if (matchIndex > 0)
                {
                    var previous = listItems[matchIndex - 1];
                    var previousActivity = DateTime.SpecifyKind(
                        previous.GetProperty("lastActivityAt").GetDateTime(),
                        DateTimeKind.Utc);
                    Assert.True(
                        previousActivity >= DateTime.SpecifyKind(expectedLastActivity, DateTimeKind.Utc),
                        "List must be sorted by LastActivityAt descending.");
                }

                if (matchIndex + 1 < listItems.GetArrayLength())
                {
                    var next = listItems[matchIndex + 1];
                    var nextActivity = DateTime.SpecifyKind(
                        next.GetProperty("lastActivityAt").GetDateTime(),
                        DateTimeKind.Utc);
                    Assert.True(
                        nextActivity <= DateTime.SpecifyKind(expectedLastActivity, DateTimeKind.Utc),
                        "List must be sorted by LastActivityAt descending.");
                }
            }

            using (var detailRequest = new HttpRequestMessage(
                       HttpMethod.Get,
                       $"/api/tickets/{ticketId}"))
            {
                using var detailResponse = await client.SendAsync(detailRequest);
                Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
                using var detailDoc = JsonDocument.Parse(
                    await detailResponse.Content.ReadAsStringAsync());
                var detail = detailDoc.RootElement;
                Assert.Equal("CustomerReplied", detail.GetProperty("status").GetString());
                Assert.Equal(ticketNumber, detail.GetProperty("ticketNumber").GetString());
                Assert.Equal(subject, detail.GetProperty("subject").GetString());
                Assert.Equal(JsonValueKind.Null, detail.GetProperty("resolvedAt").ValueKind);
                Assert.Equal(JsonValueKind.Null, detail.GetProperty("closedByUserId").ValueKind);

                var messages = detail.GetProperty("messages");
                Assert.Equal(2, messages.GetArrayLength());
                Assert.Equal(
                    "Seed before list lifecycle.",
                    messages[0].GetProperty("content").GetString());
                Assert.Equal(reopenBody, messages[1].GetProperty("content").GetString());
                Assert.Equal("Customer", messages[1].GetProperty("senderType").GetString());

                var firstCreated = messages[0].GetProperty("createdAt").GetDateTime();
                var secondCreated = messages[1].GetProperty("createdAt").GetDateTime();
                Assert.True(firstCreated <= secondCreated);
            }

            _ = loginUserId;
        }
        finally
        {
            await CleanupTicketsAsync(factory, ticketIds);
        }
    }

    private WebApplicationFactory<Program> CreateFactory(
        ControllableEmailReceiver receiver,
        RecordingEmailSender sender) =>
        baseFactory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("environment", "Development");
            builder.UseEnvironment("Development");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IEmailReceiver>();
                services.AddSingleton<IEmailReceiver>(receiver);
                services.RemoveAll<IEmailSender>();
                services.AddSingleton<IEmailSender>(sender);
            });
        });

    private WebApplicationFactory<Program> CreateFactoryWithFixedTime(
        ControllableEmailReceiver receiver,
        RecordingEmailSender sender) =>
        baseFactory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("environment", "Development");
            builder.UseEnvironment("Development");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IEmailReceiver>();
                services.AddSingleton<IEmailReceiver>(receiver);
                services.RemoveAll<IEmailSender>();
                services.AddSingleton<IEmailSender>(sender);
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(new FixedTimeProvider(FixedNow));
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

    private static string UniqueCanonicalTicketNumber()
    {
        // Canonical VS-###### so inbound TicketNumberParser matches for reopen.
        var value = (BitConverter.ToUInt32(Guid.NewGuid().ToByteArray(), 0) % 900_000) + 100_000;
        return TicketNumberFormat.Format(value);
    }

    private static void AssertEqualUtc(DateTime expected, DateTime actual)
    {
        Assert.Equal(
            DateTime.SpecifyKind(expected, DateTimeKind.Utc),
            DateTime.SpecifyKind(actual, DateTimeKind.Utc));
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

    /// <summary>
    /// Pins <c>AutoResolve.InactiveDays</c> to catalog default so CutoffUtc (now-3d)
    /// stays deterministic if another suite mutated the shared DB row.
    /// </summary>
    private static async Task EnsureDefaultInactiveDaysAsync(WebApplicationFactory<Program> factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var definition = ApplicationParameterCatalog.All.Single(
            d => d.Key == ApplicationParameterCatalog.AutoResolveInactiveDaysKey);
        var entity = await db.ApplicationParameters
            .SingleOrDefaultAsync(p => p.Key == definition.Key);
        if (entity is null)
        {
            db.Add(new ApplicationParameter(definition.Key, definition.DefaultValue, definition.Description));
        }
        else if (entity.Value != definition.DefaultValue)
        {
            entity.UpdateValue(definition.DefaultValue, DateTime.UtcNow);
        }

        await db.SaveChangesAsync();
    }

    private static async Task<List<ParkedTicket>> ParkForeignEligibleAsync(
        ApplicationDbContext db,
        DateTime cutoffUtc,
        IReadOnlyCollection<Guid> keepIds)
    {
        var foreign = await db.Tickets
            .Where(ticket =>
                ticket.Status == TicketStatus.WaitingCustomerReply
                && ticket.WaitingCustomerSince != null
                && ticket.WaitingCustomerSince <= cutoffUtc
                && !keepIds.Contains(ticket.Id))
            .ToListAsync();

        var parked = new List<ParkedTicket>(foreign.Count);
        var parkUntil = cutoffUtc.AddDays(30);
        foreach (var ticket in foreign)
        {
            parked.Add(new ParkedTicket(ticket.Id, ticket.WaitingCustomerSince));
            db.Entry(ticket).Property(nameof(Ticket.WaitingCustomerSince)).CurrentValue = parkUntil;
            db.Entry(ticket).Property(nameof(Ticket.WaitingCustomerSince)).IsModified = true;
        }

        if (foreign.Count > 0)
        {
            await db.SaveChangesAsync();
        }

        return parked;
    }

    private static async Task RestoreParkedAsync(
        WebApplicationFactory<Program> factory,
        IReadOnlyList<ParkedTicket> parked)
    {
        if (parked.Count == 0)
        {
            return;
        }

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        foreach (var item in parked)
        {
            var ticket = await db.Tickets.FindAsync(item.Id);
            if (ticket is null)
            {
                continue;
            }

            db.Entry(ticket).Property(nameof(Ticket.WaitingCustomerSince)).CurrentValue =
                item.OriginalWaitingCustomerSince;
            db.Entry(ticket).Property(nameof(Ticket.WaitingCustomerSince)).IsModified = true;
        }

        await db.SaveChangesAsync();
    }

    private static async Task CleanupTicketsAsync(
        WebApplicationFactory<Program> factory,
        IReadOnlyList<Guid> ticketIds)
    {
        if (ticketIds.Count == 0)
        {
            return;
        }

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var messages = await db.TicketMessages
            .Where(m => ticketIds.Contains(m.TicketId))
            .ToListAsync();
        db.TicketMessages.RemoveRange(messages);

        var processed = await db.ProcessedEmailMessages
            .Where(p => p.TicketId != null && ticketIds.Contains(p.TicketId.Value))
            .ToListAsync();
        // Also remove quarantine/idempotency rows created for these runs when TicketId was set.
        db.ProcessedEmailMessages.RemoveRange(processed);

        // Catch any processed rows by ticket linkage left null if quarantine-only (none expected).
        var tickets = await db.Tickets
            .Where(t => ticketIds.Contains(t.Id))
            .ToListAsync();
        db.Tickets.RemoveRange(tickets);

        await db.SaveChangesAsync();
    }

    private sealed class ControllableEmailReceiver : IEmailReceiver
    {
        private IncomingEmail? pending;

        public List<EmailReceiptHandle> Marked { get; } = [];

        public void Expose(IncomingEmail email) => pending = email;

        public void Reexpose(IncomingEmail email) => pending = email;

        public async IAsyncEnumerable<IncomingEmail> FetchUnreadAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken) { IReadOnlyList<IncomingEmail> batch = pending is null ? [] : [pending]; foreach(var m in batch){ yield return m; await Task.Yield(); } }

        public Task MarkAsProcessedAsync(
            EmailReceiptHandle receiptHandle,
            CancellationToken cancellationToken)
        {
            Marked.Add(receiptHandle);
            if (pending is not null
                && string.Equals(
                    pending.ReceiptHandle.Value,
                    receiptHandle.Value,
                    StringComparison.Ordinal))
            {
                pending = null;
            }

            return Task.CompletedTask;
        }
    }

    private sealed class RecordingEmailSender : IEmailSender
    {
        public List<EmailMessage> Sent { get; } = [];

        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
        {
            Sent.Add(message);
            return Task.CompletedTask;
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed record ParkedTicket(Guid Id, DateTime? OriginalWaitingCustomerSince);

    private sealed record MailJobPayload(
        int FetchedCount,
        int CreatedTickets,
        int CustomerReplies,
        int ReopenedTickets,
        int AlreadyProcessed,
        int AcknowledgementsSent,
        IReadOnlyList<string> CreatedTicketNumbers);

    private sealed record ResolveJobPayload(
        DateTime CutoffUtc,
        int Candidates,
        int Resolved,
        int Skipped,
        int Conflicted,
        int Failed);
}
