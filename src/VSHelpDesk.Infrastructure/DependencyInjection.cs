using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VSHelpDesk.Application.Abstractions.Authentication;
using VSHelpDesk.Application.Abstractions.Email;
using VSHelpDesk.Application.Abstractions.Parameters;
using VSHelpDesk.Application.Abstractions.Persistence;
using VSHelpDesk.Application.Abstractions.Persistence.Repositories;
using VSHelpDesk.Application.Abstractions.Security;
using VSHelpDesk.Application.Abstractions.Storage;
using VSHelpDesk.Application.Features.MailProcessing.ProcessIncomingEmails;
using VSHelpDesk.Application.Features.ScheduledJobs.ResolveInactiveTickets;
using VSHelpDesk.Application.Features.Tickets.ReadModel;
using VSHelpDesk.Infrastructure.Authentication;
using VSHelpDesk.Infrastructure.Email;
using VSHelpDesk.Infrastructure.Logging;
using VSHelpDesk.Infrastructure.Parameters;
using VSHelpDesk.Infrastructure.Persistence;
using VSHelpDesk.Infrastructure.Persistence.ReadModel;
using VSHelpDesk.Infrastructure.Persistence.Repositories;
using VSHelpDesk.Infrastructure.Persistence.Seed;
using VSHelpDesk.Infrastructure.Processing;
using VSHelpDesk.Infrastructure.Security;
using VSHelpDesk.Infrastructure.Storage;

namespace VSHelpDesk.Infrastructure;

/// <summary>
/// Infrastructure composition root. Concrete services registered per weekly plan.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddLogging();

        var connectionString = configuration.GetConnectionString("DefaultConnection");
        var provider = configuration["Database:Provider"]?.Trim();

        if (string.IsNullOrWhiteSpace(connectionString) && string.IsNullOrWhiteSpace(provider))
        {
            throw new InvalidOperationException(
                "The ConnectionStrings:DefaultConnection configuration value is required.");
        }

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            if (string.Equals(provider, "InMemory", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(provider, "In-Memory", StringComparison.OrdinalIgnoreCase))
            {
                connectionString = "VSHelpDeskDb";
            }
            else if (string.Equals(provider, "Sqlite", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(provider, "SQLite", StringComparison.OrdinalIgnoreCase))
            {
                connectionString = "Data Source=vshelpdesk.db";
            }
            else
            {
                connectionString = "Host=localhost;Database=vs_help_desk_dev;Username=postgres;Password=postgres";
            }
        }
        if (string.IsNullOrWhiteSpace(provider))
        {
            if (connectionString.StartsWith("Data Source=", StringComparison.OrdinalIgnoreCase) ||
                connectionString.EndsWith(".db", StringComparison.OrdinalIgnoreCase) ||
                connectionString.Contains(":memory:", StringComparison.OrdinalIgnoreCase))
            {
                provider = "Sqlite";
            }
            else if (connectionString.Equals("InMemory", StringComparison.OrdinalIgnoreCase) ||
                     connectionString.Contains("InMemory", StringComparison.OrdinalIgnoreCase))
            {
                provider = "InMemory";
            }
            else
            {
                provider = "Postgres";
            }
        }

        var isPostgres = provider.Equals("Postgres", StringComparison.OrdinalIgnoreCase) ||
                         provider.Equals("PostgreSQL", StringComparison.OrdinalIgnoreCase) ||
                         provider.Equals("Npgsql", StringComparison.OrdinalIgnoreCase);

        var isSqlServer = provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase) ||
                          provider.Equals("MSSQL", StringComparison.OrdinalIgnoreCase) ||
                          provider.Equals("SQLServer", StringComparison.OrdinalIgnoreCase);

        var isSqlite = provider.Equals("Sqlite", StringComparison.OrdinalIgnoreCase) ||
                       provider.Equals("SQLite", StringComparison.OrdinalIgnoreCase);

        var isInMemory = provider.Equals("InMemory", StringComparison.OrdinalIgnoreCase) ||
                         provider.Equals("In-Memory", StringComparison.OrdinalIgnoreCase);

        services.AddDbContext<ApplicationDbContext>(options =>
        {
            if (isSqlServer)
            {
                options.UseSqlServer(connectionString);
            }
            else if (isSqlite)
            {
                options.UseSqlite(connectionString);
            }
            else if (isInMemory)
            {
                options.UseInMemoryDatabase(string.IsNullOrWhiteSpace(connectionString) ? "VSHelpDeskDb" : connectionString);
            }
            else
            {
                options.UseNpgsql(connectionString);
            }
        });

        services.AddScoped<IApplicationDbContext>(
            serviceProvider => serviceProvider.GetRequiredService<ApplicationDbContext>());
        services.AddScoped<ITicketRepository, EfTicketRepository>();
        services.AddScoped<IUserRepository, EfUserRepository>();
        services.AddScoped<ITicketAttachmentRepository, EfTicketAttachmentRepository>();
        services.AddScoped<IApplicationParameterRepository, EfApplicationParameterRepository>();
        services.AddScoped<IProcessedEmailRepository, EfProcessedEmailRepository>();
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();
        services.AddScoped<ITicketListReadRepository, EfTicketListReadRepository>();
        services.AddScoped<ITicketDetailReadRepository, EfTicketDetailReadRepository>();
        services.AddScoped<IApplicationParameterReader, ApplicationParameterReader>();
        services.AddOptions<SeedUserOptions>()
            .Bind(configuration.GetSection(SeedUserOptions.SectionName));
        services.AddOptions<SeedAdminOptions>()
            .Bind(configuration.GetSection(SeedAdminOptions.SectionName));
        services.AddScoped<DevelopmentDataSeeder>();

        services.AddSingleton<IValidateOptions<AuthOptions>, AuthOptionsValidator>();
        services.AddOptions<AuthOptions>()
            .Bind(configuration.GetSection(AuthOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IPasswordHasher, PasswordHasher>();
        services.AddSingleton<ITokenService, JwtTokenService>();
        services.AddScoped<ITicketNumberGenerator, TicketNumberGenerator>();

        if (isPostgres)
        {
            services.AddSingleton<IDatabaseErrorClassifier, PostgresDatabaseErrorClassifier>();
            services.AddSingleton<IProcessIncomingEmailsGate>(serviceProvider =>
                new PostgresProcessIncomingEmailsGate(
                    connectionString,
                    serviceProvider.GetRequiredService<
                        ILogger<PostgresProcessIncomingEmailsGate>>()));
            services.AddSingleton<IResolveInactiveTicketsGate>(serviceProvider =>
                new PostgresResolveInactiveTicketsGate(
                    connectionString,
                    serviceProvider.GetRequiredService<
                        ILogger<PostgresResolveInactiveTicketsGate>>()));
        }
        else
        {
            services.AddSingleton<IDatabaseErrorClassifier, FallbackDatabaseErrorClassifier>();
            services.AddSingleton<IProcessIncomingEmailsGate, InProcessProcessIncomingEmailsGate>();
            services.AddSingleton<IResolveInactiveTicketsGate, InProcessResolveInactiveTicketsGate>();
        }

        services.AddSingleton<IValidateOptions<EmailOptions>, EmailOptionsValidator>();
        services.AddOptions<EmailOptions>()
            .Bind(configuration.GetSection(EmailOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IEmailBoundarySettings, EmailBoundarySettings>();
        services.AddSingleton<IEmailTemplateService, CorporateEmailTemplateService>();
        services.AddScoped<IEmailSender, SmtpEmailSender>();
        services.AddSingleton<HtmlToPlainTextConverter>();
        services.AddSingleton<IHtmlSanitizerService, HtmlSanitizerService>();
        services.AddScoped<IImapMailboxClient, MailKitImapMailboxClient>();
        services.AddScoped<ImapEmailReceiver>();
        services.AddScoped<FakeEmailReceiver>();
        services.AddScoped<IEmailReceiver>(serviceProvider =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<EmailOptions>>().Value;
            var mode = (options.ReceiverMode ?? "Imap").Trim();
            if (mode.Equals("Imap", StringComparison.OrdinalIgnoreCase))
            {
                return serviceProvider.GetRequiredService<ImapEmailReceiver>();
            }

            return serviceProvider.GetRequiredService<FakeEmailReceiver>();
        });

        services.AddSingleton<IValidateOptions<FileStorageOptions>, FileStorageOptionsValidator>();
        services.AddOptions<FileStorageOptions>()
            .Bind(configuration.GetSection(FileStorageOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IAttachmentUploadPolicy, ConfiguredAttachmentUploadPolicy>();
        services.AddSingleton<IFileStorage, LocalFileStorage>();

        // Singleton factory: each call opens/disposes an async scope; no scoped ctor deps.
        services.AddSingleton<IInboundEmailItemProcessorFactory, ScopedInboundEmailItemProcessorFactory>();
        services.AddSingleton<IInactiveTicketResolverFactory, ScopedInactiveTicketResolverFactory>();

        services.AddHostedService<OrphanAttachmentCleanupHostedService>();

        services.AddSingleton<ILoggerProvider, DbLoggerProvider>(sp =>
            new DbLoggerProvider(sp.GetRequiredService<IServiceScopeFactory>(), LogLevel.Error));

        return services;
    }
}
