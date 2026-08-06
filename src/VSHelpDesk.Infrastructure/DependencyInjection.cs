using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VSHelpDesk.Domain.Entities;
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
using VSHelpDesk.Application.Common.Localization;
using VSHelpDesk.Infrastructure.Localization;
using VSHelpDesk.Infrastructure.Persistence.Sequences;
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
        services.AddSingleton<IMessageProvider>(sp =>
            new LocalizedDictionaryMessageProvider(
                TurkishMessages.Messages,
                EnglishMessages.Messages,
                sp.GetService<ILogger<LocalizedDictionaryMessageProvider>>()));


        var configuredConnection = configuration.GetConnectionString("DefaultConnection");
        var provider = DatabaseProviderConfiguration.Resolve(
            configuration["Database:Provider"],
            configuredConnection);
        var connectionString = DatabaseProviderConfiguration.ResolveConnectionString(
            provider,
            configuredConnection);
        var migrationsAssembly = configuration["Database:MigrationsAssembly"]?.Trim();
        if (string.IsNullOrWhiteSpace(migrationsAssembly))
        {
            migrationsAssembly = typeof(ApplicationDbContext).Assembly.FullName;
        }

        services.AddDbContext<ApplicationDbContext>(options =>
            DatabaseProviderConfiguration.Configure(
                options,
                provider,
                connectionString,
                migrationsAssembly));

        var isPostgres = provider == DatabaseProviderKind.Postgres;
        var isSqlServer = provider == DatabaseProviderKind.SqlServer;
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
            services.AddScoped<ISequenceValueAllocator, PostgresSequenceAllocator>();
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
        else if (isSqlServer)
        {
            services.AddScoped<ISequenceValueAllocator, SqlServerSequenceAllocator>();
            services.AddSingleton<IDatabaseErrorClassifier, SqlServerDatabaseErrorClassifier>();
            services.AddSingleton<IProcessIncomingEmailsGate>(serviceProvider =>
                new SqlServerProcessIncomingEmailsGate(
                    connectionString,
                    serviceProvider.GetRequiredService<
                        ILogger<SqlServerProcessIncomingEmailsGate>>()));
            services.AddSingleton<IResolveInactiveTicketsGate>(serviceProvider =>
                new SqlServerResolveInactiveTicketsGate(
                    connectionString,
                    serviceProvider.GetRequiredService<
                        ILogger<SqlServerResolveInactiveTicketsGate>>()));
        }
        else
        {
            services.AddScoped<ISequenceValueAllocator, FallbackSequenceAllocator>();
            services.AddSingleton<IDatabaseErrorClassifier, FallbackDatabaseErrorClassifier>();
            services.AddSingleton<IProcessIncomingEmailsGate, InProcessProcessIncomingEmailsGate>();
            services.AddSingleton<IResolveInactiveTicketsGate, InProcessResolveInactiveTicketsGate>();
        }

        services.AddSingleton<IValidateOptions<EmailOptions>, EmailOptionsValidator>();
        services.AddOptions<EmailOptions>()
            .Bind(configuration.GetSection(EmailOptions.SectionName))
            .ValidateOnStart();

        services.AddSingleton<IValidateOptions<MailboxQuotaOptions>, MailboxQuotaOptionsValidator>();
        services.AddOptions<MailboxQuotaOptions>()
            .Bind(configuration.GetSection(MailboxQuotaOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IMailboxQuotaSettings>(sp => sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<MailboxQuotaOptions>>().Value);

        services.AddSingleton<IValidateOptions<EmailBrandingOptions>, EmailBrandingOptionsValidator>();
        services.AddOptions<EmailBrandingOptions>()
            .Bind(configuration.GetSection(EmailBrandingOptions.SectionName))
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
        services.AddSingleton<VSHelpDesk.Application.Common.IO.ITemporaryFileFactory, TemporaryFileFactory>();
        services.AddSingleton<LocalFileStorage>();
        services.AddSingleton<IFileStorage>(serviceProvider =>
            serviceProvider.GetRequiredService<LocalFileStorage>());
        services.AddSingleton<IFileStorageInspector>(serviceProvider =>
            serviceProvider.GetRequiredService<LocalFileStorage>());

        // Singleton factory: each call opens/disposes an async scope; no scoped ctor deps.
        services.AddSingleton<IInboundEmailItemProcessorFactory, ScopedInboundEmailItemProcessorFactory>();
        services.AddSingleton<IInactiveTicketResolverFactory, ScopedInactiveTicketResolverFactory>();

        services.AddHostedService<OrphanAttachmentCleanupHostedService>();

        services.AddSingleton<IValidateOptions<DatabaseLoggingOptions>, DatabaseLoggingOptionsValidator>();
        services.AddOptions<DatabaseLoggingOptions>()
            .Bind(configuration.GetSection(DatabaseLoggingOptions.SectionName))
            .ValidateOnStart();

        services.AddSingleton<ILogPropertySanitizer, LogPropertySanitizer>();
        services.AddSingleton<SystemLogDropMetrics>();

        services.AddSingleton<Channel<SystemLog>>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<DatabaseLoggingOptions>>().Value;
            var capacity = Math.Clamp(options.QueueCapacity, 10, 50000);
            var metrics = sp.GetRequiredService<SystemLogDropMetrics>();
            return Channel.CreateBounded<SystemLog>(
                new BoundedChannelOptions(capacity)
                {
                    FullMode = BoundedChannelFullMode.DropOldest,
                    SingleReader = true,
                    SingleWriter = false,
                    AllowSynchronousContinuations = false
                },
                _ => metrics.IncrementDroppedCount());
        });
        services.AddSingleton(sp => sp.GetRequiredService<Channel<SystemLog>>().Reader);
        services.AddSingleton(sp => sp.GetRequiredService<Channel<SystemLog>>().Writer);

        services.AddSingleton<ILoggerProvider, DbLoggerProvider>(sp =>
            new DbLoggerProvider(
                sp.GetRequiredService<ChannelWriter<SystemLog>>(),
                sp.GetRequiredService<IOptions<DatabaseLoggingOptions>>(),
                sp.GetService<ILogPropertySanitizer>(),
                sp.GetService<SystemLogDropMetrics>()));

        services.AddHostedService<DbLogBackgroundWriter>();
        services.AddHostedService<DbLogRetentionHostedService>();

        return services;
    }
}
