using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using VSHelpDesk.Application.Abstractions.Email;
using VSHelpDesk.Application.Features.MailProcessing.ProcessIncomingEmails;

using VSHelpDesk.WebAPI.IntegrationTests.Support;

namespace VSHelpDesk.WebAPI.IntegrationTests.Jobs;

public sealed class ProcessIncomingEmailsConflictTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly WebApplicationFactory<Program> factory;

    public ProcessIncomingEmailsConflictTests(CustomWebApplicationFactory factory)
    {
        this.factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("environment", "Development");
            builder.UseEnvironment("Development");
        });
    }

    [Fact]
    public async Task JobGateContention_Returns409WithoutFetchingMailbox()
    {
        var spyReceiver = new SpyEmailReceiver();
        var clientFactory = CreateContentionFactory(spyReceiver);
        var apiKey = GetJobsApiKey(clientFactory);

        using var client = clientFactory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/jobs/process-incoming-emails");
        request.Headers.Add("X-Jobs-Api-Key", apiKey);
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Equal(409, root.GetProperty("status").GetInt32());
        Assert.Equal(
            "The request conflicts with current state.",
            root.GetProperty("title").GetString());
        Assert.DoesNotContain("password", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("connection", json, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, spyReceiver.FetchCount);
    }

    [Fact]
    public async Task ConflictApplicationException_IsMappedTo409()
    {
        var clientFactory = CreateContentionFactory(new SpyEmailReceiver());
        var apiKey = GetJobsApiKey(clientFactory);

        using var client = clientFactory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/jobs/process-incoming-emails");
        request.Headers.Add("X-Jobs-Api-Key", apiKey);
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(409, doc.RootElement.GetProperty("status").GetInt32());
        Assert.Equal(
            "The request conflicts with current state.",
            doc.RootElement.GetProperty("title").GetString());
    }

    private WebApplicationFactory<Program> CreateContentionFactory(SpyEmailReceiver spyReceiver) =>
        factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("environment", "Development");
            builder.UseEnvironment("Development");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IProcessIncomingEmailsGate>();
                services.AddSingleton<IProcessIncomingEmailsGate>(new BusyGate());

                services.RemoveAll<IEmailReceiver>();
                services.AddSingleton<IEmailReceiver>(spyReceiver);
            });
        });

    private static string GetJobsApiKey(WebApplicationFactory<Program> clientFactory)
    {
        using var scope = clientFactory.Services.CreateScope();
        var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var apiKey = configuration["Jobs:ApiKey"];
        Assert.False(string.IsNullOrWhiteSpace(apiKey));
        return apiKey!;
    }

    private sealed class BusyGate : IProcessIncomingEmailsGate
    {
        public Task<IProcessIncomingEmailsLease?> TryAcquireAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IProcessIncomingEmailsLease?>(null);
    }

    private sealed class SpyEmailReceiver : IEmailReceiver
    {
        public int FetchCount { get; private set; }

        public Task<IReadOnlyList<IncomingEmail>> FetchUnreadAsync(
            CancellationToken cancellationToken = default)
        {
            FetchCount++;
            return Task.FromResult<IReadOnlyList<IncomingEmail>>([]);
        }

        public Task MarkAsProcessedAsync(
            EmailReceiptHandle receiptHandle,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
