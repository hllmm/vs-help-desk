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
    public async Task UC005_Reply_EmptyContent_Returns400()
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
    }

    [Fact]
    public async Task UC005_Reply_Success_SavesSupportMessageAndWaits()
    {
        var sender = new RecordingEmailSender();
        var replyFactory = CreateFactoryWithEmailSender(sender);
        var token = await LoginAsync(replyFactory);

        Guid ticketId;
        string ticketNumber;
        await using (var scope = replyFactory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var stamp = DateTime.UtcNow;
            ticketNumber = $"VS-R{stamp:HHmmss}";
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
        Assert.True(doc.RootElement.GetProperty("emailDelivered").GetBoolean());
        Assert.Equal("WaitingCustomerReply", doc.RootElement.GetProperty("status").GetString());
        Assert.Equal(ticketNumber, doc.RootElement.GetProperty("ticketNumber").GetString());

        Assert.Single(sender.Sent);
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
            Assert.Equal("Please try restarting the printer.", messages[0].Content);
            Assert.NotNull(messages[0].UserId);
        }
    }

    [Fact]
    public async Task BR022_Reply_SmtpFailure_KeepsMessageAndReportsError()
    {
        var sender = new RecordingEmailSender { ThrowOnSend = true };
        var replyFactory = CreateFactoryWithEmailSender(sender);
        var token = await LoginAsync(replyFactory);

        Guid ticketId;
        await using (var scope = replyFactory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var stamp = DateTime.UtcNow;
            var ticket = Ticket.Create(
                $"VS-F{stamp:HHmmss}",
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
        Assert.False(doc.RootElement.GetProperty("emailDelivered").GetBoolean());
        Assert.Contains(
            "saved",
            doc.RootElement.GetProperty("emailDeliveryError").GetString(),
            StringComparison.OrdinalIgnoreCase);
        Assert.Equal("CustomerReplied", doc.RootElement.GetProperty("status").GetString());

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
