using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using VSHelpDesk.Application.Abstractions.Email;
using VSHelpDesk.Application.Abstractions.Persistence;
using VSHelpDesk.Application.Common.Exceptions;
using VSHelpDesk.Application.Features.Tickets.AssignTicket;
using VSHelpDesk.Domain.Entities;
using VSHelpDesk.Domain.Enums;
using VSHelpDesk.Infrastructure.Persistence;
using VSHelpDesk.WebAPI.IntegrationTests.Support;

namespace VSHelpDesk.WebAPI.IntegrationTests.Tickets;

public sealed class TicketsApiTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly WebApplicationFactory<Program> factory;

    public TicketsApiTests(CustomWebApplicationFactory factory)
    {
        this.factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("environment", "Development");
            builder.UseEnvironment("Development");
        });
    }

    private WebApplicationFactory<Program> CreateFactoryWithEmailSender(IEmailSender emailSender) =>
        factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("environment", "Development");
            builder.UseEnvironment("Development");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IEmailSender>();
                services.AddSingleton(emailSender);
            });
        });

    [Fact]
    public async Task UC003_GetTickets_WithoutToken_Returns401()
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/api/tickets");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task UC003_GetTickets_WithAuthCookie_Returns200Array()
    {
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var stamp = DateTime.UtcNow;
            db.Add(Ticket.Create(
                UniqueSeedTicketNumber("VS-T"),
                "List seed",
                "Seed Customer",
                "seed@example.test",
                stamp));
            await db.SaveChangesAsync();
        }

        var (client, _, _) = await CookieAuthTestHelper.LoginAsSupportAsync(factory);
        using (client)
        {
            using var response = await client.GetAsync("/api/tickets");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            Assert.Equal(JsonValueKind.Array, doc.RootElement.ValueKind);
            Assert.True(doc.RootElement.GetArrayLength() >= 1);
        }
    }

    [Fact]
    public async Task UC004_GetTicket_UnknownId_Returns404()
    {
        var (client, _, _) = await CookieAuthTestHelper.LoginAsSupportAsync(factory);
        using (client)
        {
            using var response = await client.GetAsync($"/api/tickets/{Guid.NewGuid()}");
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
    }

    [Fact]
    public async Task GetById_WithoutToken_Returns401()
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync($"/api/tickets/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetById_UnknownTicket_Returns404WithoutRawException()
    {
        var (client, _, _) = await CookieAuthTestHelper.LoginAsSupportAsync(factory);
        using (client)
        {
            using var response = await client.GetAsync($"/api/tickets/{Guid.NewGuid()}");

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync();
            AssertSafeMissingResourceBody(body);
        }
    }

    [Fact]
    public async Task GetById_ReturnsMessagesAndAttachmentsInDeterministicOrder()
    {
        Guid ticketId = Guid.Empty;
        List<TicketMessage> expectedMessages;
        List<TicketAttachment> expectedAttachments;
        var uniqueNumber = $"VS-DO{Guid.NewGuid():N}"[..16];

        try
        {
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var stamp = new DateTime(2026, 8, 5, 12, 0, 0, DateTimeKind.Utc);
                var ticket = Ticket.Create(
                    uniqueNumber,
                    "Deterministic order subject",
                    "Ada",
                    "ada-order@example.test",
                    stamp);
                ticketId = ticket.Id;

                var sameTime = stamp.AddMinutes(5);
                var messageEarly = new TicketMessage(
                    ticket.Id,
                    MessageSenderType.Customer,
                    "Early message",
                    createdAtUtc: stamp.AddMinutes(1));
                var messageTieA = new TicketMessage(
                    ticket.Id,
                    MessageSenderType.Support,
                    "Tie message A",
                    createdAtUtc: sameTime);
                var messageTieB = new TicketMessage(
                    ticket.Id,
                    MessageSenderType.Customer,
                    "Tie message B",
                    createdAtUtc: sameTime);

                db.Add(ticket);
                db.Add(messageEarly);
                db.Add(messageTieA);
                db.Add(messageTieB);
                await db.SaveChangesAsync();

                var attachmentSame = stamp.AddMinutes(6);
                var storageKey = Guid.NewGuid().ToString("N");
                var attachmentEarly = new TicketAttachment(
                    messageEarly.Id,
                    "early.txt",
                    $"stored-early-{storageKey}.txt",
                    $"/tmp/vshd-seed/early-{storageKey}.txt",
                    "text/plain",
                    11,
                    stamp.AddMinutes(2));
                var attachmentTieA = new TicketAttachment(
                    messageTieA.Id,
                    "tie-a.pdf",
                    $"stored-tie-a-{storageKey}.pdf",
                    $"/tmp/vshd-seed/tie-a-{storageKey}.pdf",
                    "application/pdf",
                    22,
                    attachmentSame);
                var attachmentTieB = new TicketAttachment(
                    messageTieB.Id,
                    "tie-b.txt",
                    $"stored-tie-b-{storageKey}.txt",
                    $"/tmp/vshd-seed/tie-b-{storageKey}.txt",
                    "text/plain",
                    33,
                    attachmentSame);

                db.Add(attachmentEarly);
                db.Add(attachmentTieA);
                db.Add(attachmentTieB);
                await db.SaveChangesAsync();

                expectedMessages = await db.TicketMessages
                    .Where(m => m.TicketId == ticketId)
                    .OrderBy(m => m.CreatedAt)
                    .ThenBy(m => m.Id)
                    .ToListAsync();
                expectedAttachments = await db.TicketAttachments
                    .Where(a => expectedMessages.Select(m => m.Id).Contains(a.TicketMessageId))
                    .OrderBy(a => a.CreatedAt)
                    .ThenBy(a => a.Id)
                    .ToListAsync();
            }

            Assert.Equal(3, expectedMessages.Count);
            Assert.Equal(3, expectedAttachments.Count);
            Assert.Equal(expectedMessages[1].CreatedAt, expectedMessages[2].CreatedAt);
            Assert.True(expectedMessages[1].Id.CompareTo(expectedMessages[2].Id) < 0);
            Assert.Equal(expectedAttachments[1].CreatedAt, expectedAttachments[2].CreatedAt);
            Assert.True(expectedAttachments[1].Id.CompareTo(expectedAttachments[2].Id) < 0);

            var (client, _, _) = await CookieAuthTestHelper.LoginAsSupportAsync(factory);
            using (client)
            {
                using var response = await client.GetAsync($"/api/tickets/{ticketId}");

                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                var root = doc.RootElement;
                Assert.Equal(ticketId, root.GetProperty("id").GetGuid());
                Assert.Equal("Deterministic order subject", root.GetProperty("subject").GetString());

                var messages = root.GetProperty("messages");
                Assert.Equal(3, messages.GetArrayLength());
                for (var i = 0; i < expectedMessages.Count; i++)
                {
                    var expected = expectedMessages[i];
                    var actual = messages[i];
                    Assert.Equal(expected.Id, actual.GetProperty("id").GetGuid());
                    Assert.Equal(expected.SenderType.ToString(), actual.GetProperty("senderType").GetString());
                    Assert.Equal(expected.Content, actual.GetProperty("content").GetString());
                    Assert.Equal(expected.IsHtml, actual.GetProperty("isHtml").GetBoolean());
                    Assert.Equal(expected.CreatedAt, actual.GetProperty("createdAt").GetDateTime());
                }

                var attachments = root.GetProperty("attachments");
                Assert.Equal(3, attachments.GetArrayLength());
                for (var i = 0; i < expectedAttachments.Count; i++)
                {
                    var expected = expectedAttachments[i];
                    var actual = attachments[i];
                    Assert.Equal(expected.Id, actual.GetProperty("id").GetGuid());
                    Assert.Equal(expected.TicketMessageId, actual.GetProperty("ticketMessageId").GetGuid());
                    Assert.Equal(expected.FileName, actual.GetProperty("fileName").GetString());
                    Assert.Equal(expected.ContentType, actual.GetProperty("contentType").GetString());
                    Assert.Equal(expected.FileSize, actual.GetProperty("fileSize").GetInt64());
                    Assert.Equal(expected.CreatedAt, actual.GetProperty("createdAt").GetDateTime());
                }
            }
        }
        finally
        {
            if (ticketId != Guid.Empty)
            {
                await using var cleanup = factory.Services.CreateAsyncScope();
                var db = cleanup.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var ticket = await db.Tickets.FirstOrDefaultAsync(t => t.Id == ticketId);
                if (ticket is not null)
                {
                    var messageIds = await db.TicketMessages
                        .Where(m => m.TicketId == ticketId)
                        .Select(m => m.Id)
                        .ToListAsync();
                    var attachments = db.TicketAttachments
                        .Where(a => messageIds.Contains(a.TicketMessageId));
                    db.TicketAttachments.RemoveRange(attachments);
                    db.TicketMessages.RemoveRange(
                        db.TicketMessages.Where(m => m.TicketId == ticketId));
                    db.Tickets.Remove(ticket);
                    await db.SaveChangesAsync();
                }
            }
        }
    }

    [Fact]
    public async Task UC004_GetTicket_Existing_ReturnsMessagesChronological()
    {
        Guid ticketId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var stamp = DateTime.UtcNow;
            var ticket = Ticket.Create(
                UniqueSeedTicketNumber("VS-D"),
                "Detail subject",
                "Ada",
                "ada@example.test",
                stamp);
            ticketId = ticket.Id;
            db.Add(ticket);
            db.Add(new TicketMessage(
                ticket.Id,
                Domain.Enums.MessageSenderType.Customer,
                "First",
                createdAtUtc: stamp.AddMinutes(1)));
            db.Add(new TicketMessage(
                ticket.Id,
                Domain.Enums.MessageSenderType.Support,
                "Second",
                createdAtUtc: stamp.AddMinutes(2)));
            await db.SaveChangesAsync();
        }

        var (client, _, _) = await CookieAuthTestHelper.LoginAsSupportAsync(factory);
        using (client)
        {
            using var response = await client.GetAsync($"/api/tickets/{ticketId}");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            Assert.Equal("Detail subject", doc.RootElement.GetProperty("subject").GetString());
            var messages = doc.RootElement.GetProperty("messages");
            Assert.Equal(2, messages.GetArrayLength());
            Assert.Equal("First", messages[0].GetProperty("content").GetString());
            Assert.Equal("Second", messages[1].GetProperty("content").GetString());
        }
    }

    private static void AssertSafeMissingResourceBody(string body)
    {
        Assert.DoesNotContain("NotFoundException", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Exception", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("StackTrace", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(" at ", body, StringComparison.Ordinal);
        Assert.DoesNotContain("VSHelpDesk.", body, StringComparison.Ordinal);
        Assert.DoesNotContain("System.", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UC005_Reply_WithoutCookies_IsRejected()
    {
        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync(
            $"/api/tickets/{Guid.NewGuid()}/replies",
            new { content = "Hello" });
        // No vshd.auth → CSRF skipped; [Authorize] returns 401.
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Reply_Blank_Returns400WithReplyContentRequiredCode()
    {
        var (client, csrf, _) = await CookieAuthTestHelper.LoginAsSupportAsync(factory);
        using (client)
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"/api/tickets/{Guid.NewGuid()}/replies")
            {
                Content = JsonContent.Create(new { content = "   " })
            };
            CookieAuthTestHelper.AddCsrf(request, csrf);
            using var response = await client.SendAsync(request);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal("reply-content-required", doc.RootElement.GetProperty("code").GetString());
        }
    }

    [Fact]
    public async Task Reply_OverLimit_Returns400WithReplyContentTooLongCode()
    {
        Guid ticketId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var stamp = DateTime.UtcNow;
            var ticket = Ticket.Create(
                $"VS-OL{stamp:HHmmssfff}",
                "Over limit",
                "Ada",
                "ada-over@example.test",
                stamp);
            ticket.MarkAsCustomerReplied(stamp);
            ticketId = ticket.Id;
            db.Add(ticket);
            await db.SaveChangesAsync();
        }

        var (client, csrf, _) = await CookieAuthTestHelper.LoginAsSupportAsync(factory);
        using (client)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/tickets/{ticketId}/replies")
            {
                Content = JsonContent.Create(new { content = new string('x', 65_537) })
            };
            CookieAuthTestHelper.AddCsrf(request, csrf);
            using var response = await client.SendAsync(request);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal("reply-content-too-long", doc.RootElement.GetProperty("code").GetString());
        }

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Assert.Empty(await db.TicketMessages.Where(m => m.TicketId == ticketId).ToListAsync());
        }
    }

    [Fact]
    public async Task Reply_ExtraIsHtmlTrue_IsIgnoredAndStoredAsPlainText()
    {
        var sender = new RecordingEmailSender();
        var replyFactory = CreateFactoryWithEmailSender(sender);

        Guid ticketId;
        await using (var scope = replyFactory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var stamp = DateTime.UtcNow;
            var ticket = Ticket.Create(
                $"VS-H{stamp:HHmmssfff}",
                "HTML ignored",
                "Ada",
                "ada-html@example.test",
                stamp);
            ticket.MarkAsCustomerReplied(stamp);
            ticketId = ticket.Id;
            db.Add(ticket);
            await db.SaveChangesAsync();
        }

        var (client, csrf, _) = await CookieAuthTestHelper.LoginAsSupportAsync(replyFactory);
        using (client)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/tickets/{ticketId}/replies")
            {
                Content = JsonContent.Create(new
                {
                    content = "<strong>literal text</strong>",
                    isHtml = true
                })
            };
            CookieAuthTestHelper.AddCsrf(request, csrf);
            using var response = await client.SendAsync(request);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        await using (var scope = replyFactory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var messages = await db.TicketMessages
                .Where(m => m.TicketId == ticketId)
                .ToListAsync();
            Assert.Single(messages);
            Assert.Equal("<strong>literal text</strong>", messages[0].Content);
            Assert.False(messages[0].IsHtml);
        }

        Assert.Single(sender.Sent);
        Assert.False(sender.Sent[0].IsHtml);
        Assert.Equal("<strong>literal text</strong>", sender.Sent[0].Body);
    }

    [Fact]
    public async Task Reply_Success_ReturnsDeliveredAndStateUpdated()
    {
        var sender = new RecordingEmailSender();
        var replyFactory = CreateFactoryWithEmailSender(sender);

        Guid ticketId;
        string ticketNumber;
        await using (var scope = replyFactory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var stamp = DateTime.UtcNow;
            ticketNumber = $"VS-R{stamp:HHmmssfff}";
            var ticket = Ticket.Create(
                ticketNumber,
                "Reply subject",
                "Ada",
                "ada-reply@example.test",
                stamp);
            ticket.MarkAsCustomerReplied(stamp);
            ticketId = ticket.Id;
            db.Add(ticket);
            await db.SaveChangesAsync();
        }

        var (client, csrf, _) = await CookieAuthTestHelper.LoginAsSupportAsync(replyFactory);
        using (client)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/tickets/{ticketId}/replies")
            {
                Content = JsonContent.Create(new { content = "Please try restarting the printer." })
            };
            CookieAuthTestHelper.AddCsrf(request, csrf);
            using var response = await client.SendAsync(request);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            Assert.True(root.GetProperty("emailDelivered").GetBoolean());
            Assert.True(root.GetProperty("ticketStateUpdated").GetBoolean());
            Assert.Equal(JsonValueKind.Null, root.GetProperty("noticeCode").ValueKind);
            Assert.Equal("WaitingCustomerReply", root.GetProperty("status").GetString());
            Assert.Equal(ticketNumber, root.GetProperty("ticketNumber").GetString());
        }

        Assert.Single(sender.Sent);
        Assert.False(sender.Sent[0].IsHtml);
        Assert.Contains(ticketNumber, sender.Sent[0].Subject, StringComparison.Ordinal);
        Assert.Equal("ada-reply@example.test", sender.Sent[0].ToAddress);

        await using (var scope = replyFactory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var ticket = await db.Tickets.SingleAsync(t => t.Id == ticketId);
            Assert.Equal(TicketStatus.WaitingCustomerReply, ticket.Status);
            Assert.NotNull(ticket.WaitingCustomerSince);

            var messages = await db.TicketMessages
                .Where(m => m.TicketId == ticketId)
                .ToListAsync();
            Assert.Single(messages);
            Assert.Equal(MessageSenderType.Support, messages[0].SenderType);
            Assert.False(messages[0].IsHtml);
            Assert.Equal("Please try restarting the printer.", messages[0].Content);
            Assert.NotNull(messages[0].UserId);
        }
    }

    [Fact]
    public async Task Reply_SmtpFailure_ReturnsSavedOutcomeWithoutRawError()
    {
        var sender = new RecordingEmailSender { ThrowOnSend = true };
        var replyFactory = CreateFactoryWithEmailSender(sender);

        Guid ticketId;
        await using (var scope = replyFactory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var stamp = DateTime.UtcNow;
            var ticket = Ticket.Create(
                $"VS-F{stamp:HHmmssfff}",
                "Fail SMTP",
                "Bob",
                "bob-fail@example.test",
                stamp);
            ticket.MarkAsCustomerReplied(stamp);
            ticketId = ticket.Id;
            db.Add(ticket);
            await db.SaveChangesAsync();
        }

        var (client, csrf, _) = await CookieAuthTestHelper.LoginAsSupportAsync(replyFactory);
        using (client)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/tickets/{ticketId}/replies")
            {
                Content = JsonContent.Create(new { content = "VPN enabled on your account." })
            };
            CookieAuthTestHelper.AddCsrf(request, csrf);
            using var response = await client.SendAsync(request);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            Assert.False(root.GetProperty("emailDelivered").GetBoolean());
            Assert.False(root.GetProperty("ticketStateUpdated").GetBoolean());
            Assert.Equal(
                "smtp-delivery-failed",
                root.GetProperty("noticeCode").GetString());
            Assert.Equal("CustomerReplied", root.GetProperty("status").GetString());
            Assert.False(root.TryGetProperty("emailDeliveryError", out _));
            Assert.DoesNotContain("SMTP down", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("password", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("bob-fail@example.test", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("VPN enabled", json, StringComparison.OrdinalIgnoreCase);
        }

        await using (var scope = replyFactory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var ticket = await db.Tickets.SingleAsync(t => t.Id == ticketId);
            Assert.Equal(TicketStatus.CustomerReplied, ticket.Status);

            var messages = await db.TicketMessages
                .Where(m => m.TicketId == ticketId)
                .ToListAsync();
            Assert.Single(messages);
            Assert.Equal(MessageSenderType.Support, messages[0].SenderType);
            Assert.False(messages[0].IsHtml);
        }
    }

    [Fact]
    public async Task Reply_ResolvedTicket_Returns409WithoutMessageOrEmail()
    {
        var sender = new RecordingEmailSender();
        var replyFactory = CreateFactoryWithEmailSender(sender);
        Guid seedUserId;
        Guid ticketId;
        DateTime resolvedAt;
        const string customerEmail = "ada-resolved-reply@example.test";
        const string replyContent = "Should never persist on resolved ticket.";

        await using (var scope = replyFactory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            seedUserId = await GetSeedUserIdAsync(db);
            var stamp = DateTime.UtcNow;
            resolvedAt = stamp.AddMinutes(-5);
            var ticket = Ticket.Create(
                $"VS-RR{stamp:HHmmssfff}",
                "Resolved reply guard",
                "Ada",
                customerEmail,
                stamp.AddMinutes(-10));
            Assert.True(ticket.ResolveManually(resolvedAt, seedUserId));
            ticketId = ticket.Id;
            db.Add(ticket);
            await db.SaveChangesAsync();
        }

        var (client, csrf, _) = await CookieAuthTestHelper.LoginAsSupportAsync(replyFactory);
        using (client)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/tickets/{ticketId}/replies")
            {
                Content = JsonContent.Create(new { content = replyContent })
            };
            CookieAuthTestHelper.AddCsrf(request, csrf);
            using var response = await client.SendAsync(request);

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            Assert.Equal(409, doc.RootElement.GetProperty("status").GetInt32());
            Assert.Equal(
                "The request conflicts with current state.",
                doc.RootElement.GetProperty("title").GetString());
            Assert.DoesNotContain(customerEmail, json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(replyContent, json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("ResolvedTicketReplyException", json, StringComparison.Ordinal);
            Assert.DoesNotContain("Exception", json, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Empty(sender.Sent);

        await using (var scope = replyFactory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var ticket = await db.Tickets.SingleAsync(t => t.Id == ticketId);
            Assert.Equal(TicketStatus.Resolved, ticket.Status);
            Assert.Equal(seedUserId, ticket.ClosedByUserId);
            Assert.Equal(
                DateTime.SpecifyKind(resolvedAt, DateTimeKind.Utc),
                DateTime.SpecifyKind(ticket.ResolvedAt!.Value, DateTimeKind.Utc),
                TimeSpan.FromMilliseconds(1));
            Assert.Empty(await db.TicketMessages.Where(m => m.TicketId == ticketId).ToListAsync());
        }
    }

    [Fact]
    public async Task Resolve_WithoutCookies_IsRejected()
    {
        using var client = factory.CreateClient();
        using var response = await client.PostAsync($"/api/tickets/{Guid.NewGuid()}/resolve", content: null);
        // No vshd.auth → CSRF skipped; [Authorize] returns 401.
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Resolve_UnknownTicket_Returns404()
    {
        var (client, csrf, _) = await CookieAuthTestHelper.LoginAsSupportAsync(factory);
        using (client)
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"/api/tickets/{Guid.NewGuid()}/resolve");
            CookieAuthTestHelper.AddCsrf(request, csrf);
            using var response = await client.SendAsync(request);

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            AssertSafeMissingResourceBody(await response.Content.ReadAsStringAsync());
        }
    }

    [Fact]
    public async Task Resolve_OpenTicket_ReturnsExactResultAndPersistsCurrentUser()
    {
        Guid seedUserId;
        Guid ticketId;
        string ticketNumber;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            seedUserId = await GetSeedUserIdAsync(db);
            var stamp = DateTime.UtcNow;
            ticketNumber = $"VS-RO{stamp:HHmmssfff}";
            var ticket = Ticket.Create(
                ticketNumber,
                "Resolve open",
                "Ada",
                "ada-resolve-open@example.test",
                stamp);
            ticketId = ticket.Id;
            db.Add(ticket);
            await db.SaveChangesAsync();
        }

        var (client, csrf, _) = await CookieAuthTestHelper.LoginAsSupportAsync(factory);
        using (client)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/tickets/{ticketId}/resolve");
            CookieAuthTestHelper.AddCsrf(request, csrf);
            using var response = await client.SendAsync(request);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var root = doc.RootElement;
            Assert.Equal(ticketId, root.GetProperty("ticketId").GetGuid());
            Assert.Equal(ticketNumber, root.GetProperty("ticketNumber").GetString());
            Assert.Equal("Resolved", root.GetProperty("status").GetString());
            Assert.True(root.GetProperty("changed").GetBoolean());
            Assert.Equal(seedUserId, root.GetProperty("closedByUserId").GetGuid());
            Assert.True(root.TryGetProperty("resolvedAt", out _));
            Assert.True(root.TryGetProperty("updatedAt", out _));
            Assert.True(root.TryGetProperty("lastActivityAt", out _));
        }

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var ticket = await db.Tickets.SingleAsync(t => t.Id == ticketId);
            Assert.Equal(TicketStatus.Resolved, ticket.Status);
            Assert.Equal(seedUserId, ticket.ClosedByUserId);
            Assert.NotNull(ticket.ResolvedAt);
        }
    }

    [Fact]
    public async Task Resolve_AlreadyResolved_ReturnsChangedFalseAndPreservesOriginalClosure()
    {
        Guid seedUserId;
        Guid ticketId;
        DateTime originalResolvedAt;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            seedUserId = await GetSeedUserIdAsync(db);
            var stamp = DateTime.UtcNow;
            originalResolvedAt = stamp.AddMinutes(-15);
            var ticket = Ticket.Create(
                $"VS-RA{stamp:HHmmssfff}",
                "Already resolved",
                "Ada",
                "ada-resolve-again@example.test",
                stamp.AddMinutes(-30));
            Assert.True(ticket.ResolveManually(originalResolvedAt, seedUserId));
            ticketId = ticket.Id;
            db.Add(ticket);
            await db.SaveChangesAsync();
        }

        var (client, csrf, _) = await CookieAuthTestHelper.LoginAsSupportAsync(factory);
        using (client)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/tickets/{ticketId}/resolve");
            CookieAuthTestHelper.AddCsrf(request, csrf);
            using var response = await client.SendAsync(request);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var root = doc.RootElement;
            Assert.False(root.GetProperty("changed").GetBoolean());
            Assert.Equal(seedUserId, root.GetProperty("closedByUserId").GetGuid());
            Assert.Equal(
                DateTime.SpecifyKind(originalResolvedAt, DateTimeKind.Utc),
                DateTime.SpecifyKind(root.GetProperty("resolvedAt").GetDateTime(), DateTimeKind.Utc),
                TimeSpan.FromMilliseconds(1));
        }

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var ticket = await db.Tickets.SingleAsync(t => t.Id == ticketId);
            Assert.Equal(TicketStatus.Resolved, ticket.Status);
            Assert.Equal(seedUserId, ticket.ClosedByUserId);
            Assert.Equal(
                DateTime.SpecifyKind(originalResolvedAt, DateTimeKind.Utc),
                DateTime.SpecifyKind(ticket.ResolvedAt!.Value, DateTimeKind.Utc),
                TimeSpan.FromMilliseconds(1));
        }
    }

    [Fact]
    public async Task Resolve_ConcurrencyConflict_Returns409WithoutRetry()
    {
        var openTicket = Ticket.Create(
            $"VS-RC{DateTime.UtcNow:HHmmssfff}",
            "Conflict resolve",
            "Ada",
            "ada-resolve-conflict@example.test",
            DateTime.UtcNow);
        var conflictDb = new ConflictOnSaveDbContext(openTicket);
        var conflictFactory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("environment", "Development");
            builder.UseEnvironment("Development");
            builder.UseSetting("SeedUser:Enabled", "false");
            builder.UseSetting("SeedAdmin:Enabled", "false");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IApplicationDbContext>();
                services.AddSingleton<IApplicationDbContext>(conflictDb);
            });
        });

        // Login against the base host (real Users store). conflictFactory replaces
        // IApplicationDbContext with an empty in-memory stub, so seed login cannot run there.
        // Replay captured cookies onto a conflictFactory client for the resolve call.
        var (authJwt, csrf, _) = await CookieAuthTestHelper.CaptureSupportLoginAsync(factory);
        using var client = conflictFactory.CreateClient();
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/tickets/{openTicket.Id}/resolve");
        CookieAuthTestHelper.AddAuthCookies(request, authJwt, csrf);
        CookieAuthTestHelper.AddCsrf(request, csrf);
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(409, doc.RootElement.GetProperty("status").GetInt32());
        Assert.Equal(
            "The request conflicts with current state.",
            doc.RootElement.GetProperty("title").GetString());
        Assert.Equal(1, conflictDb.SaveCallCount);
        Assert.Equal(0, conflictDb.ClearTrackedCallCount);
    }

    [Fact]
    public async Task GetAssignees_WithoutSession_Returns401()
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/api/tickets/assignees");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetAssignees_ReturnsOnlyActiveUsersWithMinimalIdentity()
    {
        var inactive = await IntegrationTestUser.CreateInactiveAsync(factory.Services);
        try
        {
            var (client, _, supportUserId) =
                await CookieAuthTestHelper.LoginAsSupportAsync(factory);
            using (client)
            using (var response = await client.GetAsync("/api/tickets/assignees"))
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                Assert.Equal(JsonValueKind.Array, doc.RootElement.ValueKind);
                var rows = doc.RootElement.EnumerateArray().ToList();
                Assert.Contains(rows, row => row.GetProperty("id").GetGuid() == supportUserId);
                Assert.DoesNotContain(rows, row => row.GetProperty("id").GetGuid() == inactive.Id);
                Assert.All(rows, row =>
                {
                    Assert.Equal(
                        ["fullName", "id", "username"],
                        row.EnumerateObject().Select(property => property.Name).Order().ToArray());
                    Assert.False(string.IsNullOrWhiteSpace(row.GetProperty("fullName").GetString()));
                    Assert.False(string.IsNullOrWhiteSpace(row.GetProperty("username").GetString()));
                });
            }
        }
        finally
        {
            await IntegrationTestUser.DeleteAsync(factory.Services, inactive.Id);
        }
    }

    [Fact]
    public async Task Assign_SupportUserCanAssignAndUnassignOpenTicket()
    {
        Guid ticketId;
        DateTime originalActivity;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            originalActivity = DateTime.UtcNow.AddMinutes(-10);
            var ticket = Ticket.Create(
                UniqueSeedTicketNumber("VS-AS"),
                "Assignment API",
                "Ada",
                "ada-assign@example.test",
                originalActivity);
            ticketId = ticket.Id;
            db.Add(ticket);
            await db.SaveChangesAsync();
        }

        var (client, csrf, supportUserId) =
            await CookieAuthTestHelper.LoginAsSupportAsync(factory);
        using (client)
        {
            CookieAuthTestHelper.UseDefaultCsrfHeader(client, csrf);

            using var assignResponse = await client.PutAsJsonAsync(
                $"/api/tickets/{ticketId}/assignee",
                new { userId = (Guid?)supportUserId });
            Assert.Equal(HttpStatusCode.OK, assignResponse.StatusCode);
            using (var doc = JsonDocument.Parse(await assignResponse.Content.ReadAsStringAsync()))
            {
                Assert.Equal(ticketId, doc.RootElement.GetProperty("ticketId").GetGuid());
                Assert.Equal(supportUserId, doc.RootElement.GetProperty("assignedUserId").GetGuid());
                Assert.True(doc.RootElement.GetProperty("changed").GetBoolean());
            }

            using var unassignResponse = await client.PutAsJsonAsync(
                $"/api/tickets/{ticketId}/assignee",
                new { userId = (Guid?)null });
            Assert.Equal(HttpStatusCode.OK, unassignResponse.StatusCode);
            using (var doc = JsonDocument.Parse(await unassignResponse.Content.ReadAsStringAsync()))
            {
                Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("assignedUserId").ValueKind);
                Assert.True(doc.RootElement.GetProperty("changed").GetBoolean());
            }
        }

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var persisted = await db.Tickets.AsNoTracking().SingleAsync(ticket => ticket.Id == ticketId);
            Assert.Null(persisted.AssignedUserId);
            Assert.Equal(
                DateTime.SpecifyKind(originalActivity, DateTimeKind.Utc),
                DateTime.SpecifyKind(persisted.LastActivityAt, DateTimeKind.Utc),
                TimeSpan.FromMilliseconds(1));
        }
    }

    [Fact]
    public async Task Assign_MissingCsrfIs403_AndUnknownTicketIs404()
    {
        var (client, csrf, supportUserId) =
            await CookieAuthTestHelper.LoginAsSupportAsync(factory);
        using (client)
        {
            using var noCsrf = await client.PutAsJsonAsync(
                $"/api/tickets/{Guid.NewGuid()}/assignee",
                new { userId = (Guid?)supportUserId });
            Assert.Equal(HttpStatusCode.Forbidden, noCsrf.StatusCode);

            CookieAuthTestHelper.UseDefaultCsrfHeader(client, csrf);
            using var missing = await client.PutAsJsonAsync(
                $"/api/tickets/{Guid.NewGuid()}/assignee",
                new { userId = (Guid?)supportUserId });
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        }
    }

    [Fact]
    public async Task Assign_InactiveTargetAndResolvedTicketReturnStableCodes()
    {
        var inactive = await IntegrationTestUser.CreateInactiveAsync(factory.Services);
        Guid openTicketId;
        Guid resolvedTicketId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var stamp = DateTime.UtcNow.AddMinutes(-5);
            var closerUserId = await GetSeedUserIdAsync(db);
            var open = Ticket.Create(
                UniqueSeedTicketNumber("VS-AI"),
                "Inactive assignee",
                "Ada",
                "ada-inactive@example.test",
                stamp);
            var resolved = Ticket.Create(
                UniqueSeedTicketNumber("VS-AR"),
                "Resolved assignment",
                "Ada",
                "ada-resolved@example.test",
                stamp);
            Assert.True(resolved.ResolveManually(stamp.AddMinutes(1), closerUserId));
            openTicketId = open.Id;
            resolvedTicketId = resolved.Id;
            db.Add(open);
            db.Add(resolved);
            await db.SaveChangesAsync();
        }

        try
        {
            var (client, csrf, supportUserId) =
                await CookieAuthTestHelper.LoginAsSupportAsync(factory);
            using (client)
            {
                CookieAuthTestHelper.UseDefaultCsrfHeader(client, csrf);
                using var inactiveResponse = await client.PutAsJsonAsync(
                    $"/api/tickets/{openTicketId}/assignee",
                    new { userId = (Guid?)inactive.Id });
                Assert.Equal(HttpStatusCode.BadRequest, inactiveResponse.StatusCode);
                using (var doc = JsonDocument.Parse(await inactiveResponse.Content.ReadAsStringAsync()))
                {
                    Assert.Equal(
                        AssignTicketCodes.AssigneeNotAvailable,
                        doc.RootElement.GetProperty("code").GetString());
                }

                using var resolvedResponse = await client.PutAsJsonAsync(
                    $"/api/tickets/{resolvedTicketId}/assignee",
                    new { userId = (Guid?)supportUserId });
                Assert.Equal(HttpStatusCode.BadRequest, resolvedResponse.StatusCode);
                using (var doc = JsonDocument.Parse(await resolvedResponse.Content.ReadAsStringAsync()))
                {
                    Assert.Equal(
                        AssignTicketCodes.TicketResolved,
                        doc.RootElement.GetProperty("code").GetString());
                }
            }
        }
        finally
        {
            await IntegrationTestUser.DeleteAsync(factory.Services, inactive.Id);
        }
    }

    [Fact]
    public async Task Assign_ConcurrencyConflictReturns409WithoutRetry()
    {
        var openTicket = Ticket.Create(
            "VS-ACONFLICT",
            "Conflict assignment",
            "Ada",
            "ada-assignment-conflict@example.test",
            DateTime.UtcNow.AddHours(-1));
        Assert.True(openTicket.Assign(Guid.NewGuid(), DateTime.UtcNow.AddMinutes(-30)));
        var conflictDb = new ConflictOnSaveDbContext(openTicket);
        var conflictFactory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("environment", "Development");
            builder.UseEnvironment("Development");
            builder.UseSetting("SeedUser:Enabled", "false");
            builder.UseSetting("SeedAdmin:Enabled", "false");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IApplicationDbContext>();
                services.AddSingleton<IApplicationDbContext>(conflictDb);
            });
        });

        var (authJwt, csrf, _) = await CookieAuthTestHelper.CaptureSupportLoginAsync(factory);
        using var client = conflictFactory.CreateClient();
        using var request = new HttpRequestMessage(
            HttpMethod.Put,
            $"/api/tickets/{openTicket.Id}/assignee")
        {
            Content = JsonContent.Create(new { userId = (Guid?)null })
        };
        CookieAuthTestHelper.AddAuthCookies(request, authJwt, csrf);
        CookieAuthTestHelper.AddCsrf(request, csrf);
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(1, conflictDb.SaveCallCount);
        Assert.Equal(0, conflictDb.ClearTrackedCallCount);
    }

    /// <summary>
    /// Non-canonical test seed numbers. Millisecond stamp alone collides under parallel
    /// runs and dirty shared Postgres (IX_Tickets_TicketNumber); Guid suffix makes them unique.
    /// Truncated to Ticket.TicketNumber max length (32).
    /// </summary>
    private static string UniqueSeedTicketNumber(string prefix)
    {
        var candidate = $"{prefix}{DateTime.UtcNow:HHmmssfff}-{Guid.NewGuid():N}";
        return candidate.Length <= 32 ? candidate : candidate[..32];
    }

    private static async Task<Guid> GetSeedUserIdAsync(ApplicationDbContext db)
    {
        var userId = await db.Users
            .Where(user => user.Username == CustomWebApplicationFactory.TestSeedUsername)
            .Select(user => user.Id)
            .SingleOrDefaultAsync();
        Assert.NotEqual(Guid.Empty, userId);
        return userId;
    }

    private sealed class RecordingEmailSender : IEmailSender
    {
        public bool ThrowOnSend { get; init; }
        public List<EmailMessage> Sent { get; } = [];

        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
        {
            if (ThrowOnSend)
            {
                throw new InvalidOperationException("SMTP down");
            }

            Sent.Add(message);
            return Task.CompletedTask;
        }
    }

    private sealed class ConflictOnSaveDbContext(Ticket ticket) : IApplicationDbContext
    {
        public int SaveCallCount { get; private set; }
        public int ClearTrackedCallCount { get; private set; }

        public IQueryable<User> Users => Array.Empty<User>().AsQueryable();
        public IQueryable<Ticket> Tickets => new[] { ticket }.AsQueryable();
        public IQueryable<TicketMessage> TicketMessages =>
            Array.Empty<TicketMessage>().AsQueryable();
        public IQueryable<TicketAttachment> TicketAttachments =>
            Array.Empty<TicketAttachment>().AsQueryable();
        public IQueryable<ProcessedEmailMessage> ProcessedEmailMessages =>
            Array.Empty<ProcessedEmailMessage>().AsQueryable();

        public IQueryable<ApplicationParameter> ApplicationParameters =>
            Array.Empty<ApplicationParameter>().AsQueryable();

        public IQueryable<ParameterChangeLog> ParameterChangeLogs =>
            Array.Empty<ParameterChangeLog>().AsQueryable();

        public void Add<TEntity>(TEntity entity) where TEntity : class
        {
        }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveCallCount++;
            throw new OptimisticConcurrencyException("Simulated resolve concurrency conflict.");
        }

        public void ClearTrackedChanges() => ClearTrackedCallCount++;
    }
}
