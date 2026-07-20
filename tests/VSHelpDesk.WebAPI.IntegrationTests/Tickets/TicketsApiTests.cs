using System.Net;
using System.Net.Http.Headers;
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
using VSHelpDesk.Domain.Entities;
using VSHelpDesk.Domain.Enums;
using VSHelpDesk.Infrastructure.Persistence;

namespace VSHelpDesk.WebAPI.IntegrationTests.Tickets;

public sealed class TicketsApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> factory;

    public TicketsApiTests(WebApplicationFactory<Program> factory)
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
    public async Task UC003_GetTickets_WithBearer_Returns200Array()
    {
        var token = await LoginAsync();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var stamp = DateTime.UtcNow;
            db.Add(Ticket.Create(
                $"VS-T{stamp:HHmmss}",
                "List seed",
                "Seed Customer",
                "seed@example.test",
                stamp));
            await db.SaveChangesAsync();
        }

        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/tickets");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(JsonValueKind.Array, doc.RootElement.ValueKind);
        Assert.True(doc.RootElement.GetArrayLength() >= 1);
    }

    [Fact]
    public async Task UC004_GetTicket_UnknownId_Returns404()
    {
        var token = await LoginAsync();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/tickets/{Guid.NewGuid()}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
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
        var token = await LoginAsync();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/tickets/{Guid.NewGuid()}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        AssertSafeMissingResourceBody(body);
    }

    [Fact]
    public async Task GetById_ReturnsMessagesAndAttachmentsInDeterministicOrder()
    {
        var token = await LoginAsync();
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
                var attachmentEarly = new TicketAttachment(
                    messageEarly.Id,
                    "early.txt",
                    "stored-early.txt",
                    "/tmp/vshd-seed/early.txt",
                    "text/plain",
                    11,
                    stamp.AddMinutes(2));
                var attachmentTieA = new TicketAttachment(
                    messageTieA.Id,
                    "tie-a.pdf",
                    "stored-tie-a.pdf",
                    "/tmp/vshd-seed/tie-a.pdf",
                    "application/pdf",
                    22,
                    attachmentSame);
                var attachmentTieB = new TicketAttachment(
                    messageTieB.Id,
                    "tie-b.txt",
                    "stored-tie-b.txt",
                    "/tmp/vshd-seed/tie-b.txt",
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

            using var client = factory.CreateClient();
            using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/tickets/{ticketId}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await client.SendAsync(request);

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
        var token = await LoginAsync();
        Guid ticketId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var stamp = DateTime.UtcNow;
            var ticket = Ticket.Create(
                $"VS-D{stamp:HHmmss}",
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

        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/tickets/{ticketId}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("Detail subject", doc.RootElement.GetProperty("subject").GetString());
        var messages = doc.RootElement.GetProperty("messages");
        Assert.Equal(2, messages.GetArrayLength());
        Assert.Equal("First", messages[0].GetProperty("content").GetString());
        Assert.Equal("Second", messages[1].GetProperty("content").GetString());
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
    public async Task UC005_Reply_WithoutToken_Returns401()
    {
        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync(
            $"/api/tickets/{Guid.NewGuid()}/replies",
            new { content = "Hello" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Reply_Blank_Returns400WithReplyContentRequiredCode()
    {
        var token = await LoginAsync();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/tickets/{Guid.NewGuid()}/replies")
        {
            Content = JsonContent.Create(new { content = "   " })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("reply-content-required", doc.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Reply_OverLimit_Returns400WithReplyContentTooLongCode()
    {
        var token = await LoginAsync();
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

        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/tickets/{ticketId}/replies")
        {
            Content = JsonContent.Create(new { content = new string('x', 65_537) })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("reply-content-too-long", doc.RootElement.GetProperty("code").GetString());

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
        var token = await LoginAsync();

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

        using var client = replyFactory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/tickets/{ticketId}/replies")
        {
            Content = JsonContent.Create(new
            {
                content = "<strong>literal text</strong>",
                isHtml = true
            })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

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
        // Login against the fixture host (stable user-secrets); JWT validates on reply host too.
        var token = await LoginAsync();

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

        using var client = replyFactory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/tickets/{ticketId}/replies")
        {
            Content = JsonContent.Create(new { content = "Please try restarting the printer." })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
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
        var token = await LoginAsync();

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

        using var client = replyFactory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/tickets/{ticketId}/replies")
        {
            Content = JsonContent.Create(new { content = "VPN enabled on your account." })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
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

    private async Task<string> LoginAsync(WebApplicationFactory<Program>? appFactory = null)
    {
        var host = appFactory ?? factory;
        using var scope = host.Services.CreateScope();
        var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var username = configuration["SeedUser:Username"];
        var password = configuration["SeedUser:Password"];
        Assert.False(string.IsNullOrWhiteSpace(username));
        Assert.False(string.IsNullOrWhiteSpace(password));

        using var client = host.CreateClient();
        using var loginResponse = await client.PostAsJsonAsync(
            "/api/auth/login",
            new { username, password });
        loginResponse.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await loginResponse.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("accessToken").GetString()!;
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
}
