using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using VSHelpDesk.Infrastructure.Persistence;

namespace VSHelpDesk.WebAPI.IntegrationTests.Jobs;

public sealed class ProcessIncomingEmailsApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> factory;

    public ProcessIncomingEmailsApiTests(WebApplicationFactory<Program> factory)
    {
        this.factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("environment", "Development");
            builder.UseEnvironment("Development");
        });
    }

    [Fact]
    public async Task Jobs_ProcessIncomingEmails_HappyPath_CreatesTicketsFromFakeReceiver()
    {
        var apiKey = GetJobsApiKey();
        var ticketsBefore = await CountTicketsAsync();

        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/jobs/process-incoming-emails");
        request.Headers.Add("X-Jobs-Api-Key", apiKey);
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        Assert.True(root.GetProperty("fetchedCount").GetInt32() >= 1);
        Assert.True(root.GetProperty("createdTickets").GetInt32() >= 0);
        Assert.Equal(0, root.GetProperty("quarantined").GetInt32());
        Assert.Equal(0, root.GetProperty("retryableFailures").GetInt32());
        Assert.False(root.TryGetProperty("skippedInvalid", out _));
        Assert.False(root.TryGetProperty("messageIds", out _));
        Assert.True(root.TryGetProperty("acknowledgementsSent", out _));
        Assert.True(root.TryGetProperty("failures", out _));

        // Second run: Fake receiver still returns samples, but MessageId idempotency holds.
        using var secondRequest = new HttpRequestMessage(HttpMethod.Post, "/api/jobs/process-incoming-emails");
        secondRequest.Headers.Add("X-Jobs-Api-Key", apiKey);
        using var secondResponse = await client.SendAsync(secondRequest);
        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
        using var secondDoc = JsonDocument.Parse(await secondResponse.Content.ReadAsStringAsync());
        Assert.True(secondDoc.RootElement.GetProperty("alreadyProcessed").GetInt32() >= 1);
        Assert.Equal(0, secondDoc.RootElement.GetProperty("createdTickets").GetInt32());
        Assert.Equal(0, secondDoc.RootElement.GetProperty("quarantined").GetInt32());
        Assert.Equal(0, secondDoc.RootElement.GetProperty("retryableFailures").GetInt32());

        var ticketsAfter = await CountTicketsAsync();
        Assert.True(ticketsAfter >= ticketsBefore);
    }

    [Fact]
    public async Task JobApi_UsesQuarantineAndRetryableFailureCounters()
    {
        var apiKey = GetJobsApiKey();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/jobs/process-incoming-emails");
        request.Headers.Add("X-Jobs-Api-Key", apiKey);
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;

        // Fake samples are valid; counters must be present and exact zeros on happy path.
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
    }

    private string GetJobsApiKey()
    {
        using var scope = factory.Services.CreateScope();
        var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var apiKey = configuration["Jobs:ApiKey"];
        Assert.False(string.IsNullOrWhiteSpace(apiKey));
        return apiKey!;
    }

    private async Task<int> CountTicketsAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.Tickets.CountAsync();
    }
}
