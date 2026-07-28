using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using VSHelpDesk.Application.Abstractions.Persistence;
using VSHelpDesk.Domain.Entities;
using VSHelpDesk.Infrastructure;
using VSHelpDesk.Infrastructure.Persistence;

namespace VSHelpDesk.Infrastructure.UnitTests.Persistence;

public sealed class ApplicationDbContextTests
{
    [Fact]
    public void Model_MapsUserWithRequiredConstraints()
    {
        using var context = CreateMetadataContext();

        var entityType = context.Model.FindEntityType(typeof(User));
        Assert.NotNull(entityType);
        Assert.Equal("Users", entityType.GetTableName());
        Assert.Equal(
            [nameof(User.Id)],
            entityType.FindPrimaryKey()!.Properties.Select(property => property.Name));

        var fullName = entityType.FindProperty(nameof(User.FullName))!;
        var username = entityType.FindProperty(nameof(User.Username))!;
        var email = entityType.FindProperty(nameof(User.Email))!;
        var passwordHash = entityType.FindProperty(nameof(User.PasswordHash))!;
        var securityVersion = entityType.FindProperty(nameof(User.SecurityVersion))!;
        var isActive = entityType.FindProperty(nameof(User.IsActive))!;
        var createdAt = entityType.FindProperty(nameof(User.CreatedAt))!;
        var lastLoginAt = entityType.FindProperty(nameof(User.LastLoginAt))!;

        Assert.Equal(200, fullName.GetMaxLength());
        Assert.Equal(100, username.GetMaxLength());
        Assert.Equal(255, email.GetMaxLength());
        Assert.False(fullName.IsNullable);
        Assert.False(username.IsNullable);
        Assert.False(email.IsNullable);
        Assert.False(passwordHash.IsNullable);
        Assert.False(securityVersion.IsNullable);
        Assert.Equal(1, securityVersion.GetDefaultValue());
        Assert.False(isActive.IsNullable);
        Assert.False(createdAt.IsNullable);
        Assert.True(lastLoginAt.IsNullable);
        Assert.Equal("timestamp with time zone", createdAt.GetColumnType());
        Assert.Equal("timestamp with time zone", lastLoginAt.GetColumnType());
        Assert.Contains(
            entityType.GetIndexes(),
            index => index.IsUnique &&
                index.Properties.Select(property => property.Name).SequenceEqual([nameof(User.Username)]));
    }

    [Fact]
    public void Model_MapsTicketTicketMessageAndProcessedEmailWithRequiredConstraints()
    {
        using var context = CreateMetadataContext();

        var ticketType = context.Model.FindEntityType(typeof(Ticket));
        Assert.NotNull(ticketType);
        Assert.Equal("Tickets", ticketType.GetTableName());
        Assert.Equal(32, ticketType.FindProperty(nameof(Ticket.TicketNumber))!.GetMaxLength());
        Assert.Equal(500, ticketType.FindProperty(nameof(Ticket.Subject))!.GetMaxLength());
        Assert.Equal(200, ticketType.FindProperty(nameof(Ticket.CustomerName))!.GetMaxLength());
        Assert.Equal(255, ticketType.FindProperty(nameof(Ticket.CustomerEmail))!.GetMaxLength());
        Assert.False(ticketType.FindProperty(nameof(Ticket.TicketNumber))!.IsNullable);
        Assert.False(ticketType.FindProperty(nameof(Ticket.Subject))!.IsNullable);
        Assert.False(ticketType.FindProperty(nameof(Ticket.Status))!.IsNullable);
        Assert.Equal(
            "timestamp with time zone",
            ticketType.FindProperty(nameof(Ticket.LastActivityAt))!.GetColumnType());
        Assert.Contains(
            ticketType.GetIndexes(),
            index => index.IsUnique &&
                index.Properties.Select(property => property.Name)
                    .SequenceEqual([nameof(Ticket.TicketNumber)]));

        var messageType = context.Model.FindEntityType(typeof(TicketMessage));
        Assert.NotNull(messageType);
        Assert.Equal("TicketMessages", messageType.GetTableName());
        Assert.False(messageType.FindProperty(nameof(TicketMessage.Content))!.IsNullable);
        Assert.False(messageType.FindProperty(nameof(TicketMessage.SenderType))!.IsNullable);
        Assert.Equal(
            "timestamp with time zone",
            messageType.FindProperty(nameof(TicketMessage.CreatedAt))!.GetColumnType());
        Assert.Contains(
            messageType.GetForeignKeys(),
            foreignKey => foreignKey.PrincipalEntityType.ClrType == typeof(Ticket) &&
                foreignKey.Properties.Select(property => property.Name)
                    .SequenceEqual([nameof(TicketMessage.TicketId)]) &&
                foreignKey.DeleteBehavior == DeleteBehavior.Restrict);
        Assert.Contains(
            messageType.GetIndexes(),
            index => index.Properties.Select(property => property.Name)
                .SequenceEqual([nameof(TicketMessage.TicketId), nameof(TicketMessage.CreatedAt)]));

        var processedType = context.Model.FindEntityType(typeof(ProcessedEmailMessage));
        Assert.NotNull(processedType);
        Assert.Equal("ProcessedEmailMessages", processedType.GetTableName());
        Assert.Equal(998, processedType.FindProperty(nameof(ProcessedEmailMessage.IdempotencyKey))!.GetMaxLength());
        Assert.False(processedType.FindProperty(nameof(ProcessedEmailMessage.IdempotencyKey))!.IsNullable);
        Assert.Equal(
            998,
            processedType.FindProperty(nameof(ProcessedEmailMessage.SourceMessageId))!.GetMaxLength());
        Assert.Equal(
            500,
            processedType.FindProperty(nameof(ProcessedEmailMessage.ProcessingNote))!.GetMaxLength());
        Assert.Equal(
            "timestamp with time zone",
            processedType.FindProperty(nameof(ProcessedEmailMessage.ProcessedAt))!.GetColumnType());
        var unique = Assert.Single(processedType.GetIndexes(), index => index.IsUnique);
        Assert.Equal("UX_ProcessedEmailMessages_IdempotencyKey", unique.GetDatabaseName());
        Assert.Equal(
            [nameof(ProcessedEmailMessage.IdempotencyKey)],
            unique.Properties.Select(property => property.Name));

        var version = ticketType.FindProperty(nameof(Ticket.Version))!;
        Assert.True(version.IsConcurrencyToken);
        Assert.Equal(
            Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.OnAddOrUpdate,
            version.ValueGenerated);

        var attachmentType = context.Model.FindEntityType(typeof(TicketAttachment));
        Assert.NotNull(attachmentType);
        Assert.Equal("TicketAttachments", attachmentType.GetTableName());
        Assert.Equal(255, attachmentType.FindProperty(nameof(TicketAttachment.FileName))!.GetMaxLength());
        Assert.Equal(260, attachmentType.FindProperty(nameof(TicketAttachment.StoredFileName))!.GetMaxLength());
        Assert.False(attachmentType.FindProperty(nameof(TicketAttachment.ContentType))!.IsNullable);
        Assert.Equal(
            "timestamp with time zone",
            attachmentType.FindProperty(nameof(TicketAttachment.CreatedAt))!.GetColumnType());
        Assert.Contains(
            attachmentType.GetForeignKeys(),
            foreignKey => foreignKey.PrincipalEntityType.ClrType == typeof(TicketMessage) &&
                foreignKey.Properties.Select(property => property.Name)
                    .SequenceEqual([nameof(TicketAttachment.TicketMessageId)]) &&
                foreignKey.DeleteBehavior == DeleteBehavior.Restrict);
        Assert.Contains(
            attachmentType.GetIndexes(),
            index => index.IsUnique &&
                index.Properties.Select(property => property.Name)
                    .SequenceEqual([nameof(TicketAttachment.StoredFileName)]));
    }

    [Fact]
    public void Model_MapsParameterChangeLogWithKeyAndChangedAtIndex()
    {
        using var context = CreateMetadataContext();

        var logType = context.Model.FindEntityType(typeof(ParameterChangeLog));
        Assert.NotNull(logType);
        Assert.Equal("ParameterChangeLogs", logType.GetTableName());
        Assert.Equal(200, logType.FindProperty(nameof(ParameterChangeLog.ParameterKey))!.GetMaxLength());
        Assert.Equal(4000, logType.FindProperty(nameof(ParameterChangeLog.OldValue))!.GetMaxLength());
        Assert.Equal(4000, logType.FindProperty(nameof(ParameterChangeLog.NewValue))!.GetMaxLength());
        Assert.False(logType.FindProperty(nameof(ParameterChangeLog.ParameterKey))!.IsNullable);
        Assert.False(logType.FindProperty(nameof(ParameterChangeLog.OldValue))!.IsNullable);
        Assert.False(logType.FindProperty(nameof(ParameterChangeLog.NewValue))!.IsNullable);
        Assert.False(logType.FindProperty(nameof(ParameterChangeLog.ChangedByUserId))!.IsNullable);
        Assert.Equal(
            "timestamp with time zone",
            logType.FindProperty(nameof(ParameterChangeLog.ChangedAt))!.GetColumnType());
        Assert.Contains(
            logType.GetIndexes(),
            index => index.Properties.Select(property => property.Name)
                .SequenceEqual([
                    nameof(ParameterChangeLog.ParameterKey),
                    nameof(ParameterChangeLog.ChangedAt)
                ]));
    }

    [Fact]
    public void AddInfrastructure_MissingConnectionString_ThrowsClearConfigurationError()
    {
        var configuration = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();

        var exception = Assert.Throws<InvalidOperationException>(
            () => services.AddInfrastructure(configuration));

        Assert.Contains("ConnectionStrings:DefaultConnection", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AddInfrastructure_ResolvesSameScopedConcreteAndAbstractionContext()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Database=metadata_test;Username=test_user"
            })
            .Build();
        var services = new ServiceCollection();
        services.AddInfrastructure(configuration);
        using var serviceProvider = services.BuildServiceProvider();
        using var scope = serviceProvider.CreateScope();

        var concrete = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var abstraction = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

        Assert.Same(concrete, abstraction);
    }

    [Fact]
    public void ApplicationDbContextFactory_MissingEnvironmentConnection_ThrowsClearConfigurationError()
    {
        const string environmentKey = "ConnectionStrings__DefaultConnection";
        var originalValue = Environment.GetEnvironmentVariable(environmentKey);

        try
        {
            Environment.SetEnvironmentVariable(environmentKey, null);
            var factory = new ApplicationDbContextFactory();

            var exception = Assert.Throws<InvalidOperationException>(
                () => factory.CreateDbContext([]));

            Assert.Contains("ConnectionStrings:DefaultConnection", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable(environmentKey, originalValue);
        }
    }

    private static ApplicationDbContext CreateMetadataContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=metadata_test;Username=test_user")
            .Options;
        return new ApplicationDbContext(options);
    }
}
