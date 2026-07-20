using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using VSHelpDesk.Application.Abstractions.Persistence;
using VSHelpDesk.Application.Features.ScheduledJobs.ResolveInactiveTickets;
using VSHelpDesk.Domain.Entities;
using VSHelpDesk.Infrastructure.Persistence;

namespace VSHelpDesk.WebAPI.IntegrationTests.Jobs;

public sealed class ResolveInactiveTicketsConflictTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> factory;

    public ResolveInactiveTicketsConflictTests(WebApplicationFactory<Program> factory)
    {
        this.factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("environment", "Development");
            builder.UseEnvironment("Development");
        });
    }

    [Fact]
    public async Task ResolveInactive_GateContention_Returns409WithoutCandidateQuery()
    {
        var spy = new TicketsQuerySpy();
        var clientFactory = CreateContentionFactory(spy);
        var apiKey = GetJobsApiKey(clientFactory);

        using var client = clientFactory.CreateClient();
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/jobs/resolve-inactive-tickets");
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
        Assert.DoesNotContain("resolve-inactive-tickets", json, StringComparison.Ordinal);
        Assert.Equal(0, spy.TicketsEnumerationCount);
    }

    private WebApplicationFactory<Program> CreateContentionFactory(TicketsQuerySpy spy) =>
        factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("environment", "Development");
            builder.UseEnvironment("Development");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IResolveInactiveTicketsGate>();
                services.AddSingleton<IResolveInactiveTicketsGate>(new BusyGate());

                // Wrap the real EF context so development seed still has async providers,
                // while the job handler's Tickets enumeration is observed.
                services.RemoveAll<IApplicationDbContext>();
                services.AddScoped<IApplicationDbContext>(sp =>
                    new SpyApplicationDbContext(
                        sp.GetRequiredService<ApplicationDbContext>(),
                        spy));
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

    private sealed class BusyGate : IResolveInactiveTicketsGate
    {
        public Task<IResolveInactiveTicketsLease?> TryAcquireAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IResolveInactiveTicketsLease?>(null);
    }

    private sealed class TicketsQuerySpy
    {
        public int TicketsEnumerationCount { get; set; }
    }

    private sealed class SpyApplicationDbContext(
        ApplicationDbContext inner,
        TicketsQuerySpy spy) : IApplicationDbContext
    {
        public IQueryable<User> Users => inner.Users;

        public IQueryable<Ticket> Tickets
        {
            get
            {
                spy.TicketsEnumerationCount++;
                return inner.Tickets;
            }
        }

        public IQueryable<TicketMessage> TicketMessages => inner.TicketMessages;

        public IQueryable<TicketAttachment> TicketAttachments => inner.TicketAttachments;

        public IQueryable<ProcessedEmailMessage> ProcessedEmailMessages =>
            inner.ProcessedEmailMessages;

        public void Add<TEntity>(TEntity entity) where TEntity : class =>
            ((IApplicationDbContext)inner).Add(entity);

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
            inner.SaveChangesAsync(cancellationToken);

        public void ClearTrackedChanges() =>
            ((IApplicationDbContext)inner).ClearTrackedChanges();
    }
}
