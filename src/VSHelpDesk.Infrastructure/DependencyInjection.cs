using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using VSHelpDesk.Application.Abstractions.Authentication;
using VSHelpDesk.Application.Abstractions.Email;
using VSHelpDesk.Application.Abstractions.Persistence;
using VSHelpDesk.Application.Abstractions.Storage;
using VSHelpDesk.Infrastructure.Authentication;
using VSHelpDesk.Infrastructure.Email;
using VSHelpDesk.Infrastructure.Persistence;
using VSHelpDesk.Infrastructure.Persistence.Seed;
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
        var connectionString = configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "The ConnectionStrings:DefaultConnection configuration value is required.");
        }

        services.AddDbContext<ApplicationDbContext>(options => options.UseNpgsql(connectionString));
        services.AddScoped<IApplicationDbContext>(
            serviceProvider => serviceProvider.GetRequiredService<ApplicationDbContext>());
        services.AddOptions<SeedUserOptions>()
            .Bind(configuration.GetSection(SeedUserOptions.SectionName));
        services.AddScoped<DevelopmentDataSeeder>();

        services.AddSingleton<IValidateOptions<AuthOptions>, AuthOptionsValidator>();
        services.AddOptions<AuthOptions>()
            .Bind(configuration.GetSection(AuthOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IPasswordHasher, PasswordHasher>();
        services.AddSingleton<ITokenService, JwtTokenService>();
        services.AddScoped<ITicketNumberGenerator, TicketNumberGenerator>();

        services.AddSingleton<IValidateOptions<EmailOptions>, EmailOptionsValidator>();
        services.AddOptions<EmailOptions>()
            .Bind(configuration.GetSection(EmailOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IEmailBoundarySettings, EmailBoundarySettings>();
        services.AddScoped<IEmailSender, SmtpEmailSender>();
        services.AddScoped<IEmailReceiver>(serviceProvider =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<EmailOptions>>().Value;
            var mode = (options.ReceiverMode ?? "Fake").Trim();
            if (mode.Equals("Imap", StringComparison.OrdinalIgnoreCase))
            {
                return serviceProvider.GetRequiredService<NotConfiguredImapEmailReceiver>();
            }

            return serviceProvider.GetRequiredService<FakeEmailReceiver>();
        });
        services.AddScoped<FakeEmailReceiver>();
        services.AddScoped<NotConfiguredImapEmailReceiver>();

        services.AddSingleton<IValidateOptions<FileStorageOptions>, FileStorageOptionsValidator>();
        services.AddOptions<FileStorageOptions>()
            .Bind(configuration.GetSection(FileStorageOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IAttachmentUploadPolicy, ConfiguredAttachmentUploadPolicy>();
        services.AddSingleton<IFileStorage, LocalFileStorage>();

        return services;
    }
}
