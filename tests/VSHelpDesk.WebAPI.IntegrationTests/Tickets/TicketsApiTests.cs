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
    public async Task UC003_GetTickets_OmittedQueryParameters_ReturnsDefaultPageAndCounts()
    {
        var tickets = await SeedOrderedListTicketsAsync(53);
        try
        {
            var (client, _, _) = await CookieAuthTestHelper.LoginAsSupportAsync(factory);
            using (client)
            using (var response = await client.GetAsync("/api/tickets"))
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                var root = doc.RootElement;

                Assert.Equal(JsonValueKind.Object, root.ValueKind);
                Assert.Equal(50, root.GetProperty("items").GetArrayLength());
                Assert.True(root.GetProperty("hasMore").GetBoolean());
                var cursor = root.GetProperty("nextCursor").GetString();
                Assert.False(string.IsNullOrWhiteSpace(cursor));
                Assert.DoesNotContain("VS-", cursor, StringComparison.Ordinal);

                var counts = root.GetProperty("counts");
                Assert.Equal(
                    ["all", "customerReplied", "new", "resolved", "waitingCustomerReply"],
                    counts.EnumerateObject().Select(property => property.Name).Order().ToArray());
                Assert.True(counts.GetProperty("all").GetInt32() >= tickets.Count);
            }
        }
        finally
        {
            await DeleteTicketsAsync(tickets.Select(ticket => ticket.Id));
        }
    }

    [Fact]
    public async Task UC003_GetTickets_FollowingCursorReturnsRemainingTicketsWithoutOverlap()
    {
        var searchToken = $"page{Guid.NewGuid():N}";
        var tickets = await SeedOrderedListTicketsAsync(53, searchToken);
        try
        {
            var expectedIds = tickets.Select(ticket => ticket.Id).ToArray();
            var (client, _, _) = await CookieAuthTestHelper.LoginAsSupportAsync(factory);
            using (client)
            {
                using var firstResponse = await client.GetAsync(
                    $"/api/tickets?search={Uri.EscapeDataString(searchToken)}");
                Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
                using var firstDoc = JsonDocument.Parse(await firstResponse.Content.ReadAsStringAsync());
                var firstIds = firstDoc.RootElement.GetProperty("items")
                    .EnumerateArray()
                    .Select(item => item.GetProperty("id").GetGuid())
                    .ToArray();
                var cursor = firstDoc.RootElement.GetProperty("nextCursor").GetString();

                Assert.Equal(expectedIds[..50], firstIds);
                Assert.True(firstDoc.RootElement.GetProperty("hasMore").GetBoolean());
                Assert.False(string.IsNullOrWhiteSpace(cursor));

                using var nextResponse = await client.GetAsync(
                    $"/api/tickets?search={Uri.EscapeDataString(searchToken)}&cursor={Uri.EscapeDataString(cursor!)}");
                Assert.Equal(HttpStatusCode.OK, nextResponse.StatusCode);
                using var nextDoc = JsonDocument.Parse(await nextResponse.Content.ReadAsStringAsync());
                var nextIds = nextDoc.RootElement.GetProperty("items")
                    .EnumerateArray()
                    .Select(item => item.GetProperty("id").GetGuid())
                    .ToArray();

                Assert.Equal(expectedIds[50..], nextIds);
                Assert.False(nextDoc.RootElement.GetProperty("hasMore").GetBoolean());
                Assert.Equal(JsonValueKind.Null, nextDoc.RootElement.GetProperty("nextCursor").ValueKind);
                Assert.Empty(firstIds.Intersect(nextIds));
            }
        }
        finally
        {
            await DeleteTicketsAsync(tickets.Select(ticket => ticket.Id));
        }
    }

    [Fact]
    public async Task UC003_GetTickets_PageSizeAboveMaximumIsCappedAt100()
    {
        var searchToken = $"cap{Guid.NewGuid():N}";
        var tickets = await SeedOrderedListTicketsAsync(101, searchToken);
        try
        {
            var (client, _, _) = await CookieAuthTestHelper.LoginAsSupportAsync(factory);
            using (client)
            using (var response = await client.GetAsync(
                $"/api/tickets?search={Uri.EscapeDataString(searchToken)}&pageSize=500"))
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                Assert.Equal(100, doc.RootElement.GetProperty("items").GetArrayLength());
                Assert.True(doc.RootElement.GetProperty("hasMore").GetBoolean());
            }
        }
        finally
        {
            await DeleteTicketsAsync(tickets.Select(ticket => ticket.Id));
        }
    }

    [Fact]
    public async Task UC003_GetTickets_StatusFiltersItemsButCountsPrecedeStatusAndSearchMatchesFields()
    {
        var groupToken = $"group{Guid.NewGuid():N}";
        var emailToken = $"email{Guid.NewGuid():N}";
        var subjectToken = $"subject{Guid.NewGuid():N}";
        List<Ticket> tickets;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var closerId = await GetSeedUserIdAsync(db);
            var stamp = DateTime.UtcNow.AddYears(20);
            var newTicket = Ticket.Create(
                UniqueSeedTicketNumber("VS-LN"),
                $"New {groupToken}",
                groupToken,
                $"{emailToken}@example.test",
                stamp);
            var waitingTicket = Ticket.Create(
                UniqueSeedTicketNumber("VS-LW"),
                $"Waiting {groupToken}",
                groupToken,
                $"waiting-{groupToken}@example.test",
                stamp.AddMinutes(-1));
            waitingTicket.MarkAsWaitingCustomerReply(stamp.AddMinutes(-1));
            var repliedTicket = Ticket.Create(
                UniqueSeedTicketNumber("VS-LC"),
                $"Replied {groupToken}",
                groupToken,
                $"replied-{groupToken}@example.test",
                stamp.AddMinutes(-2));
            repliedTicket.MarkAsCustomerReplied(stamp.AddMinutes(-2));
            var resolvedTicket = Ticket.Create(
                UniqueSeedTicketNumber("VS-LR"),
                $"{subjectToken} {groupToken}",
                groupToken,
                $"resolved-{groupToken}@example.test",
                stamp.AddMinutes(-3));
            Assert.True(resolvedTicket.ResolveManually(stamp.AddMinutes(-3), closerId));
            tickets = [newTicket, waitingTicket, repliedTicket, resolvedTicket];
            db.AddRange(tickets);
            await db.SaveChangesAsync();
        }

        try
        {
            var (client, _, _) = await CookieAuthTestHelper.LoginAsSupportAsync(factory);
            using (client)
            {
                using var statusResponse = await client.GetAsync(
                    $"/api/tickets?status=Resolved&search={Uri.EscapeDataString(groupToken)}");
                Assert.Equal(HttpStatusCode.OK, statusResponse.StatusCode);
                using var statusDoc = JsonDocument.Parse(await statusResponse.Content.ReadAsStringAsync());
                var root = statusDoc.RootElement;
                var item = Assert.Single(root.GetProperty("items").EnumerateArray());
                Assert.Equal("Resolved", item.GetProperty("status").GetString());
                var counts = root.GetProperty("counts");
                Assert.Equal(4, counts.GetProperty("all").GetInt32());
                Assert.Equal(1, counts.GetProperty("new").GetInt32());
                Assert.Equal(1, counts.GetProperty("waitingCustomerReply").GetInt32());
                Assert.Equal(1, counts.GetProperty("customerReplied").GetInt32());
                Assert.Equal(1, counts.GetProperty("resolved").GetInt32());

                using var emailResponse = await client.GetAsync(
                    $"/api/tickets?search={Uri.EscapeDataString(emailToken)}");
                Assert.Equal(HttpStatusCode.OK, emailResponse.StatusCode);
                using var emailDoc = JsonDocument.Parse(await emailResponse.Content.ReadAsStringAsync());
                Assert.Equal(
                    tickets[0].Id,
                    Assert.Single(emailDoc.RootElement.GetProperty("items").EnumerateArray())
                        .GetProperty("id").GetGuid());

                using var subjectResponse = await client.GetAsync(
                    $"/api/tickets?search={Uri.EscapeDataString(subjectToken)}");
                Assert.Equal(HttpStatusCode.OK, subjectResponse.StatusCode);
                using var subjectDoc = JsonDocument.Parse(await subjectResponse.Content.ReadAsStringAsync());
                Assert.Equal(
                    tickets[3].Id,
                    Assert.Single(subjectDoc.RootElement.GetProperty("items").EnumerateArray())
                        .GetProperty("id").GetGuid());
            }
        }
        finally
        {
            await DeleteTicketsAsync(tickets.Select(ticket => ticket.Id));
        }
    }

    [Theory]
    [InlineData("/api/tickets?search=x", "ticket-search-too-short")]
    [InlineData("/api/tickets?search=xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx", "ticket-search-too-long")]
    [InlineData("/api/tickets?cursor=not-a-valid-cursor", "invalid-ticket-list-cursor")]
    public async Task UC003_GetTickets_InvalidQueryReturnsSafe400WithStableCode(
        string path,
        string expectedCode)
    {
        var (client, _, _) = await CookieAuthTestHelper.LoginAsSupportAsync(factory);
        using (client)
        using (var response = await client.GetAsync(path))
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
            var body = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);
            Assert.Equal(
                ["code", "status", "title"],
                doc.RootElement.EnumerateObject().Select(property => property.Name).Order().ToArray());
            Assert.Equal(400, doc.RootElement.GetProperty("status").GetInt32());
            Assert.False(string.IsNullOrWhiteSpace(doc.RootElement.GetProperty("title").GetString()));
            Assert.Equal(expectedCode, doc.RootElement.GetProperty("code").GetString());
            AssertSafeValidationBody(body);
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

    [Fact]
    public async Task GetById_LongHistoryReturnsBoundedInitialPageAndOlderPagesWithoutOverlap()
    {
        var fixture = await SeedMessageHistoryAsync(205, [1, 100, 101, 205]);

        try
        {
            var storageOrder = fixture.Messages
                .OrderByDescending(message => message.CreatedAt)
                .ThenByDescending(message => message.Id)
                .ToList();
            var (client, _, _) = await CookieAuthTestHelper.LoginAsSupportAsync(factory);
            using (client)
            {
                using var detailResponse = await client.GetAsync($"/api/tickets/{fixture.Ticket.Id}");
                Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
                using var detailDoc = JsonDocument.Parse(
                    await detailResponse.Content.ReadAsStringAsync());
                var detail = detailDoc.RootElement;
                var firstIds = detail.GetProperty("messages")
                    .EnumerateArray()
                    .Select(message => message.GetProperty("id").GetGuid())
                    .ToArray();
                var firstCursor = detail.GetProperty("nextMessageCursor").GetString();

                Assert.Equal(
                    storageOrder.Take(100).Reverse().Select(message => message.Id),
                    firstIds);
                Assert.True(detail.GetProperty("hasMoreMessages").GetBoolean());
                Assert.False(string.IsNullOrWhiteSpace(firstCursor));
                Assert.All(
                    detail.GetProperty("attachments").EnumerateArray(),
                    attachment => Assert.Contains(
                        attachment.GetProperty("ticketMessageId").GetGuid(),
                        firstIds));

                using var secondResponse = await client.GetAsync(
                    $"/api/tickets/{fixture.Ticket.Id}/messages?pageSize=100&cursor={Uri.EscapeDataString(firstCursor!)}");
                Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
                using var secondDoc = JsonDocument.Parse(
                    await secondResponse.Content.ReadAsStringAsync());
                var second = secondDoc.RootElement;
                var secondIds = second.GetProperty("messages")
                    .EnumerateArray()
                    .Select(message => message.GetProperty("id").GetGuid())
                    .ToArray();
                var secondCursor = second.GetProperty("nextCursor").GetString();

                Assert.Equal(
                    storageOrder.Skip(100).Take(100).Reverse().Select(message => message.Id),
                    secondIds);
                Assert.True(second.GetProperty("hasMore").GetBoolean());
                Assert.False(string.IsNullOrWhiteSpace(secondCursor));

                using var thirdResponse = await client.GetAsync(
                    $"/api/tickets/{fixture.Ticket.Id}/messages?pageSize=100&cursor={Uri.EscapeDataString(secondCursor!)}");
                Assert.Equal(HttpStatusCode.OK, thirdResponse.StatusCode);
                using var thirdDoc = JsonDocument.Parse(
                    await thirdResponse.Content.ReadAsStringAsync());
                var third = thirdDoc.RootElement;
                var thirdIds = third.GetProperty("messages")
                    .EnumerateArray()
                    .Select(message => message.GetProperty("id").GetGuid())
                    .ToArray();

                Assert.Equal(
                    storageOrder.Skip(200).Take(5).Reverse().Select(message => message.Id),
                    thirdIds);
                Assert.False(third.GetProperty("hasMore").GetBoolean());
                Assert.Equal(JsonValueKind.Null, third.GetProperty("nextCursor").ValueKind);
                Assert.Equal(
                    205,
                    firstIds.Concat(secondIds).Concat(thirdIds).Distinct().Count());
            }
        }
        finally
        {
            await DeleteTicketHistoryAsync(fixture.Ticket.Id);
        }
    }

    [Fact]
    public async Task GetMessages_ExistingTicketReturnsExactPageFieldsAndMetadataOnly()
    {
        var fixture = await SeedMessageHistoryAsync(3, [3]);

        try
        {
            var storageOrder = fixture.Messages
                .OrderByDescending(message => message.CreatedAt)
                .ThenByDescending(message => message.Id)
                .ToList();
            var expectedIds = storageOrder.Take(2).Reverse().Select(message => message.Id).ToArray();
            var (client, _, _) = await CookieAuthTestHelper.LoginAsSupportAsync(factory);
            using (client)
            using (var response = await client.GetAsync(
                $"/api/tickets/{fixture.Ticket.Id}/messages?pageSize=2"))
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                var root = doc.RootElement;

                Assert.Equal(
                    ["attachments", "hasMore", "messages", "nextCursor"],
                    root.EnumerateObject().Select(property => property.Name).Order().ToArray());
                Assert.Equal(
                    expectedIds,
                    root.GetProperty("messages")
                        .EnumerateArray()
                        .Select(message => message.GetProperty("id").GetGuid()));
                Assert.All(
                    root.GetProperty("messages").EnumerateArray(),
                    message =>
                    {
                        Assert.Equal(
                            ["content", "createdAt", "id", "isHtml", "senderType", "userId"],
                            message.EnumerateObject().Select(property => property.Name).Order().ToArray());
                        Assert.Contains("<tag>", message.GetProperty("content").GetString());
                    });
                var attachment = Assert.Single(root.GetProperty("attachments").EnumerateArray());
                Assert.Equal(
                    ["contentType", "createdAt", "fileName", "fileSize", "id", "ticketMessageId"],
                    attachment.EnumerateObject().Select(property => property.Name).Order().ToArray());
                Assert.Contains(attachment.GetProperty("ticketMessageId").GetGuid(), expectedIds);
                Assert.False(attachment.TryGetProperty("filePath", out _));
                Assert.False(attachment.TryGetProperty("storedFileName", out _));
                Assert.True(root.GetProperty("hasMore").GetBoolean());
                Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("nextCursor").GetString()));
            }
        }
        finally
        {
            await DeleteTicketHistoryAsync(fixture.Ticket.Id);
        }
    }

    [Fact]
    public async Task GetMessages_PageSizeIsCappedAt200AndAdvertisedCursorCanReachEmptyFinalPage()
    {
        var fixture = await SeedMessageHistoryAsync(201);

        try
        {
            var storageOrder = fixture.Messages
                .OrderByDescending(message => message.CreatedAt)
                .ThenByDescending(message => message.Id)
                .ToList();
            var (client, _, _) = await CookieAuthTestHelper.LoginAsSupportAsync(factory);
            using (client)
            {
                using var firstResponse = await client.GetAsync(
                    $"/api/tickets/{fixture.Ticket.Id}/messages?pageSize=500");
                Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
                using var firstDoc = JsonDocument.Parse(await firstResponse.Content.ReadAsStringAsync());
                var first = firstDoc.RootElement;
                var cursor = first.GetProperty("nextCursor").GetString();

                Assert.Equal(200, first.GetProperty("messages").GetArrayLength());
                Assert.True(first.GetProperty("hasMore").GetBoolean());
                Assert.False(string.IsNullOrWhiteSpace(cursor));

                await using (var scope = factory.Services.CreateAsyncScope())
                {
                    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                    await db.TicketMessages
                        .Where(message => message.Id == storageOrder[200].Id)
                        .ExecuteDeleteAsync();
                }

                using var finalResponse = await client.GetAsync(
                    $"/api/tickets/{fixture.Ticket.Id}/messages?pageSize=500&cursor={Uri.EscapeDataString(cursor!)}");
                Assert.Equal(HttpStatusCode.OK, finalResponse.StatusCode);
                using var finalDoc = JsonDocument.Parse(await finalResponse.Content.ReadAsStringAsync());
                Assert.Empty(finalDoc.RootElement.GetProperty("messages").EnumerateArray());
                Assert.Empty(finalDoc.RootElement.GetProperty("attachments").EnumerateArray());
                Assert.False(finalDoc.RootElement.GetProperty("hasMore").GetBoolean());
                Assert.Equal(JsonValueKind.Null, finalDoc.RootElement.GetProperty("nextCursor").ValueKind);
            }
        }
        finally
        {
            await DeleteTicketHistoryAsync(fixture.Ticket.Id);
        }
    }

    [Fact]
    public async Task GetMessages_UnknownTicketReturnsSafe404()
    {
        var (client, _, _) = await CookieAuthTestHelper.LoginAsSupportAsync(factory);
        using (client)
        using (var response = await client.GetAsync($"/api/tickets/{Guid.NewGuid()}/messages"))
        {
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            AssertSafeMissingResourceBody(await response.Content.ReadAsStringAsync());
        }
    }

    [Fact]
    public async Task GetMessages_InvalidCursorReturnsSafe400WithStableCode()
    {
        var (client, _, _) = await CookieAuthTestHelper.LoginAsSupportAsync(factory);
        using (client)
        using (var response = await client.GetAsync(
            $"/api/tickets/{Guid.NewGuid()}/messages?cursor=not-a-valid-cursor"))
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);
            Assert.Equal(
                "invalid-ticket-message-cursor",
                doc.RootElement.GetProperty("code").GetString());
            AssertSafeValidationBody(body);
        }
    }

    [Fact]
    public async Task GetMessages_WithoutTokenReturns401()
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync($"/api/tickets/{Guid.NewGuid()}/messages");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
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

    private async Task<List<Ticket>> SeedOrderedListTicketsAsync(
        int count,
        string? searchToken = null)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var stamp = DateTime.UtcNow.AddYears(20);
        var tickets = Enumerable.Range(0, count)
            .Select(index => Ticket.Create(
                UniqueSeedTicketNumber("VS-LP"),
                searchToken is null ? $"Paged ticket {index}" : $"{searchToken} ticket {index}",
                "Paged Customer",
                searchToken is null
                    ? $"paged-{Guid.NewGuid():N}@example.test"
                    : $"{searchToken}-{index}@example.test",
                stamp.AddMinutes(-index)))
            .ToList();
        db.AddRange(tickets);
        await db.SaveChangesAsync();
        return tickets;
    }

    private async Task<TicketHistoryFixture> SeedMessageHistoryAsync(
        int messageCount,
        IReadOnlyCollection<int>? attachmentMessageIndexes = null)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var stamp = new DateTime(2026, 8, 6, 8, 0, 0, DateTimeKind.Utc);
        var ticket = Ticket.Create(
            UniqueSeedTicketNumber("VS-MP"),
            "Paged message history",
            "Ada",
            $"paged-history-{Guid.NewGuid():N}@example.test",
            stamp);
        var messages = Enumerable.Range(1, messageCount)
            .Select(index => new TicketMessage(
                ticket.Id,
                index % 2 == 0 ? MessageSenderType.Support : MessageSenderType.Customer,
                $"Literal message {index} <tag>",
                createdAtUtc: stamp.AddMinutes((index - 1) / 2)))
            .ToList();
        var attachments = (attachmentMessageIndexes ?? [])
            .Select(index => new TicketAttachment(
                messages[index - 1].Id,
                $"message-{index}.txt",
                $"stored-{Guid.NewGuid():N}.txt",
                $"/tmp/vshd-api-{Guid.NewGuid():N}.txt",
                "text/plain",
                index,
                messages[index - 1].CreatedAt.AddSeconds(1)))
            .ToList();

        db.Add(ticket);
        db.AddRange(messages);
        db.AddRange(attachments);
        await db.SaveChangesAsync();

        return new TicketHistoryFixture(ticket, messages);
    }

    private async Task DeleteTicketHistoryAsync(Guid ticketId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var messageIds = await db.TicketMessages
            .Where(message => message.TicketId == ticketId)
            .Select(message => message.Id)
            .ToArrayAsync();
        await db.TicketAttachments
            .Where(attachment => messageIds.Contains(attachment.TicketMessageId))
            .ExecuteDeleteAsync();
        await db.TicketMessages
            .Where(message => message.TicketId == ticketId)
            .ExecuteDeleteAsync();
        await db.Tickets
            .Where(ticket => ticket.Id == ticketId)
            .ExecuteDeleteAsync();
    }

    private async Task DeleteTicketsAsync(IEnumerable<Guid> ticketIds)
    {
        var ids = ticketIds.ToArray();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Tickets.Where(ticket => ids.Contains(ticket.Id)).ExecuteDeleteAsync();
    }

    private static void AssertSafeValidationBody(string body)
    {
        Assert.DoesNotContain("RequestValidationException", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Exception", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("StackTrace", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Host=", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Database=", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Password=", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ConnectionString", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Npgsql", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SQL", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SELECT ", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("INSERT ", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UPDATE ", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DELETE ", body, StringComparison.OrdinalIgnoreCase);
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

    private sealed record TicketHistoryFixture(
        Ticket Ticket,
        IReadOnlyList<TicketMessage> Messages);

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
