using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
using VSHelpDesk.Application.Abstractions.Persistence;
using VSHelpDesk.Application.Features.Tickets.ReadModel;
using VSHelpDesk.Domain.Entities;
using VSHelpDesk.Infrastructure;
using VSHelpDesk.Infrastructure.Persistence;
using VSHelpDesk.Infrastructure.Persistence.Migrations;
using VSHelpDesk.Infrastructure.Persistence.ReadModel;

namespace VSHelpDesk.Infrastructure.UnitTests.Persistence;

[Collection("Environment variable tests")]
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
                index.GetDatabaseName() == "IX_Tickets_TicketNumber" &&
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
                .SequenceEqual([
                    nameof(TicketMessage.TicketId),
                    nameof(TicketMessage.CreatedAt),
                    nameof(TicketMessage.Id)
                ]));

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
    public void Model_ConfiguresTicketReadIndexesAndTrigramExtension()
    {
        using var context = CreateMetadataContext();
        var model = context.GetService<IDesignTimeModel>().Model;

        Assert.Contains(
            model.GetPostgresExtensions(),
            extension => extension.Name == "pg_trgm");

        var ticketType = model.FindEntityType(typeof(Ticket))!;
        AssertIndex(
            ticketType,
            "IX_Tickets_LastActivityAt_TicketNumber",
            [nameof(Ticket.LastActivityAt), nameof(Ticket.TicketNumber)],
            [true, false]);
        AssertIndex(
            ticketType,
            "IX_Tickets_Status_LastActivityAt_TicketNumber",
            [nameof(Ticket.Status), nameof(Ticket.LastActivityAt), nameof(Ticket.TicketNumber)],
            [false, true, false]);
        AssertIndex(
            ticketType,
            "IX_Tickets_Status_WaitingCustomerSince_Id",
            [nameof(Ticket.Status), nameof(Ticket.WaitingCustomerSince), nameof(Ticket.Id)],
            [false, false, false]);

        AssertTrigramIndex(ticketType, "IX_Tickets_TicketNumber_Trgm", nameof(Ticket.TicketNumber));
        AssertTrigramIndex(ticketType, "IX_Tickets_Subject_Trgm", nameof(Ticket.Subject));
        AssertTrigramIndex(ticketType, "IX_Tickets_CustomerName_Trgm", nameof(Ticket.CustomerName));
        AssertTrigramIndex(ticketType, "IX_Tickets_CustomerEmail_Trgm", nameof(Ticket.CustomerEmail));
    }

    [Fact]
    public void Model_ConfiguresTicketDetailReadIndexesWithoutRedundantPrefixIndexes()
    {
        using var context = CreateMetadataContext();
        var model = context.GetService<IDesignTimeModel>().Model;

        var messageType = model.FindEntityType(typeof(TicketMessage))!;
        AssertIndex(
            messageType,
            "IX_TicketMessages_TicketId_CreatedAt_Id",
            [nameof(TicketMessage.TicketId), nameof(TicketMessage.CreatedAt), nameof(TicketMessage.Id)],
            [false, true, true]);
        Assert.DoesNotContain(
            messageType.GetIndexes(),
            index => index.GetDatabaseName() == "IX_TicketMessages_TicketId_CreatedAt");

        var attachmentType = model.FindEntityType(typeof(TicketAttachment))!;
        AssertIndex(
            attachmentType,
            "IX_TicketAttachments_TicketMessageId_CreatedAt_Id",
            [
                nameof(TicketAttachment.TicketMessageId),
                nameof(TicketAttachment.CreatedAt),
                nameof(TicketAttachment.Id)
            ],
            [false, false, false]);
        Assert.DoesNotContain(
            attachmentType.GetIndexes(),
            index => index.GetDatabaseName() == "IX_TicketAttachments_TicketMessageId");
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
    public void AddInfrastructure_RegistersTicketListReadRepositoryAsScoped()
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
        using var firstScope = serviceProvider.CreateScope();
        using var secondScope = serviceProvider.CreateScope();

        var first = firstScope.ServiceProvider.GetRequiredService<ITicketListReadRepository>();
        var sameScope = firstScope.ServiceProvider.GetRequiredService<ITicketListReadRepository>();
        var otherScope = secondScope.ServiceProvider.GetRequiredService<ITicketListReadRepository>();

        Assert.IsType<EfTicketListReadRepository>(first);
        Assert.Same(first, sameScope);
        Assert.NotSame(first, otherScope);
    }

    [Fact]
    public void AddInfrastructure_RegistersTicketDetailReadRepositoryAsScoped()
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
        using var firstScope = serviceProvider.CreateScope();
        using var secondScope = serviceProvider.CreateScope();

        var first = firstScope.ServiceProvider.GetRequiredService<ITicketDetailReadRepository>();
        var sameScope = firstScope.ServiceProvider.GetRequiredService<ITicketDetailReadRepository>();
        var otherScope = secondScope.ServiceProvider.GetRequiredService<ITicketDetailReadRepository>();

        Assert.IsType<EfTicketDetailReadRepository>(first);
        Assert.Same(first, sameScope);
        Assert.NotSame(first, otherScope);
    }

    [Fact]
    public void ApplicationDbContextFactory_MissingEnvironmentConnection_ThrowsClearConfigurationError()
    {
        const string connectionKey = "ConnectionStrings__DefaultConnection";
        const string providerKey = "Database__Provider";
        var originalConnection = Environment.GetEnvironmentVariable(connectionKey);
        var originalProvider = Environment.GetEnvironmentVariable(providerKey);

        try
        {
            Environment.SetEnvironmentVariable(connectionKey, null);
            Environment.SetEnvironmentVariable(providerKey, "Postgres");
            var factory = new ApplicationDbContextFactory();

            var exception = Assert.Throws<InvalidOperationException>(
                () => factory.CreateDbContext([]));

            Assert.Contains("ConnectionStrings:DefaultConnection", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable(connectionKey, originalConnection);
            Environment.SetEnvironmentVariable(providerKey, originalProvider);
        }
    }

    [Theory]
    [InlineData("Postgres", "Host=localhost;Database=test;Username=test", "Npgsql.EntityFrameworkCore.PostgreSQL")]
    [InlineData("Sqlite", "Data Source=test.db", "Microsoft.EntityFrameworkCore.Sqlite")]
    public void ApplicationDbContextFactory_UsesConfiguredProvider(
        string provider,
        string connectionString,
        string expectedProviderName)
    {
        const string connectionKey = "ConnectionStrings__DefaultConnection";
        const string providerKey = "Database__Provider";
        var originalConnection = Environment.GetEnvironmentVariable(connectionKey);
        var originalProvider = Environment.GetEnvironmentVariable(providerKey);

        try
        {
            Environment.SetEnvironmentVariable(connectionKey, connectionString);
            Environment.SetEnvironmentVariable(providerKey, provider);

            using var context = new ApplicationDbContextFactory().CreateDbContext([]);

            Assert.Equal(expectedProviderName, context.Database.ProviderName);
        }
        finally
        {
            Environment.SetEnvironmentVariable(connectionKey, originalConnection);
            Environment.SetEnvironmentVariable(providerKey, originalProvider);
        }
    }

    [Fact]
    public void Model_MapsOnlyVersionToXmin_AndOrdinaryUintNotMapped()
    {
        using var context = CreateMetadataContext();

        // Only Version:uint should be mapped to xmin
        foreach (var entityType in context.Model.GetEntityTypes())
        {
            foreach (var prop in entityType.GetProperties())
            {
                var columnName = prop.GetColumnName(StoreObjectIdentifier.Table(entityType.GetTableName()!, entityType.GetSchema()));
                if (columnName == "xmin")
                {
                    Assert.Equal("Version", prop.Name);
                    Assert.Equal(typeof(uint), prop.ClrType);
                    Assert.True(prop.IsConcurrencyToken);
                    Assert.Equal("xid", prop.GetColumnType());
                }
            }
        }

        var ticketType = context.Model.FindEntityType(typeof(Ticket))!;
        var versionProp = ticketType.FindProperty(nameof(Ticket.Version))!;
        var versionColumn = versionProp.GetColumnName(StoreObjectIdentifier.Table("Tickets", null));
        Assert.Equal("xmin", versionColumn);
        Assert.Equal("xid", versionProp.GetColumnType());
        Assert.True(versionProp.IsConcurrencyToken);
        Assert.Equal(ValueGenerated.OnAddOrUpdate, versionProp.ValueGenerated);

        // Verify ordinary test-only uint is NOT mapped to xmin via a dummy model
        var dummyOptions = new DbContextOptionsBuilder<DummyXminContext>()
            .UseNpgsql("Host=localhost;Database=dummy_test;Username=test")
            .Options;
        using var dummyContext = new DummyXminContext(dummyOptions);
        var dummyType = dummyContext.Model.FindEntityType(typeof(DummyWithUint))!;
        var ordinaryProp = dummyType.FindProperty(nameof(DummyWithUint.SomeCounter))!;
        var ordinaryColumn = ordinaryProp.GetColumnName(StoreObjectIdentifier.Table("DummyWithUints", null));
        Assert.NotEqual("xmin", ordinaryColumn);
        Assert.False(ordinaryProp.IsConcurrencyToken);
        // SQLite mapping should be integer, not xid
        var sqliteOptions = new DbContextOptionsBuilder<DummyXminContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;
        using var sqliteContext = new DummyXminContext(sqliteOptions);
        var sqliteType = sqliteContext.Model.FindEntityType(typeof(DummyWithUint))!;
        var sqliteProp = sqliteType.FindProperty(nameof(DummyWithUint.SomeCounter))!;
        Assert.NotEqual("xmin", sqliteProp.GetColumnName(StoreObjectIdentifier.Table("DummyWithUints", null)));
        Assert.False(sqliteProp.IsConcurrencyToken);
        var sqliteVersion = sqliteContext.Model.FindEntityType(typeof(DummyWithVersion))!.FindProperty(nameof(DummyWithVersion.Version))!;
        Assert.Equal("integer", sqliteVersion.GetColumnType());
        Assert.False(sqliteVersion.IsConcurrencyToken);
    }

    [Fact]
    public void Model_MapsUserVersionForPostgresAndSqlite()
    {
        using var postgresContext = CreateMetadataContext();
        var postgresUser = postgresContext.Model.FindEntityType(typeof(User))!;
        var postgresVersion = postgresUser.FindProperty("Version");

        Assert.NotNull(postgresVersion);
        Assert.Equal(
            "xmin",
            postgresVersion.GetColumnName(StoreObjectIdentifier.Table("Users", null)));
        Assert.Equal("xid", postgresVersion.GetColumnType());
        Assert.True(postgresVersion.IsConcurrencyToken);
        Assert.Equal(ValueGenerated.OnAddOrUpdate, postgresVersion.ValueGenerated);

        var sqliteOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;
        using var sqliteContext = new ApplicationDbContext(sqliteOptions);
        var sqliteUser = sqliteContext.Model.FindEntityType(typeof(User))!;
        var sqliteVersion = sqliteUser.FindProperty("Version");

        Assert.NotNull(sqliteVersion);
        Assert.Equal("integer", sqliteVersion.GetColumnType());
        Assert.False(sqliteVersion.IsConcurrencyToken);
        Assert.Equal(ValueGenerated.Never, sqliteVersion.ValueGenerated);
    }

    [Fact]
    public void AddUserVersionMigration_DoesNotContainPhysicalXminColumnOperations()
    {
        var migration = new AddUserVersionProbe();

        Assert.DoesNotContain(
            migration.CapturedUpOperations,
            operation => operation is AddColumnOperation { Name: "xmin" });
        Assert.DoesNotContain(
            migration.CapturedDownOperations,
            operation => operation is DropColumnOperation { Name: "xmin" });
    }

    private sealed class DummyWithUint
    {
        public Guid Id { get; set; }
        public uint SomeCounter { get; set; }
    }

    private sealed class DummyWithVersion
    {
        public Guid Id { get; set; }
        public uint Version { get; set; }
    }

    private sealed class AddUserVersionProbe : AddUserVersion
    {
        public IReadOnlyList<MigrationOperation> CapturedUpOperations => GetOperations(Up);

        public IReadOnlyList<MigrationOperation> CapturedDownOperations => GetOperations(Down);

        private static IReadOnlyList<MigrationOperation> GetOperations(
            Action<MigrationBuilder> buildOperations)
        {
            var migrationBuilder = new MigrationBuilder("Npgsql.EntityFrameworkCore.PostgreSQL");
            buildOperations(migrationBuilder);
            return migrationBuilder.Operations;
        }
    }

    private sealed class DummyXminContext : DbContext
    {
        public DummyXminContext(DbContextOptions<DummyXminContext> options) : base(options) { }
        public DbSet<DummyWithUint> DummyWithUints => Set<DummyWithUint>();
        public DbSet<DummyWithVersion> DummyWithVersions => Set<DummyWithVersion>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            var isPostgres = Database.IsNpgsql();
            // Replicate ApplicationDbContext's Version-only xmin logic
            foreach (var entityType in modelBuilder.Model.GetEntityTypes())
            {
                var versionProperty = entityType.FindProperty("Version");
                if (versionProperty != null && versionProperty.ClrType == typeof(uint))
                {
                    if (isPostgres)
                    {
                        versionProperty.SetColumnType("xid");
                        versionProperty.SetColumnName("xmin");
                        versionProperty.IsConcurrencyToken = true;
                        versionProperty.ValueGenerated = ValueGenerated.OnAddOrUpdate;
                    }
                    else
                    {
                        versionProperty.SetColumnType("integer");
                        versionProperty.IsConcurrencyToken = false;
                        versionProperty.ValueGenerated = ValueGenerated.Never;
                    }
                }
            }
            base.OnModelCreating(modelBuilder);
        }
    }

    private static ApplicationDbContext CreateMetadataContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=metadata_test;Username=test_user")
            .Options;
        return new ApplicationDbContext(options);
    }

    private static void AssertIndex(
        Microsoft.EntityFrameworkCore.Metadata.IEntityType entityType,
        string databaseName,
        IReadOnlyList<string> properties,
        IReadOnlyList<bool> descending)
    {
        var index = Assert.Single(
            entityType.GetIndexes(),
            candidate => candidate.GetDatabaseName() == databaseName);

        Assert.Equal(properties, index.Properties.Select(property => property.Name));
        Assert.Equal(
            descending,
            index.IsDescending ?? Enumerable.Repeat(false, index.Properties.Count).ToArray());
    }

    private static void AssertTrigramIndex(
        Microsoft.EntityFrameworkCore.Metadata.IEntityType entityType,
        string databaseName,
        string property)
    {
        var index = Assert.Single(
            entityType.GetIndexes(),
            candidate => candidate.GetDatabaseName() == databaseName);

        Assert.Equal([property], index.Properties.Select(indexProperty => indexProperty.Name));
        Assert.False(index.IsUnique);
        Assert.Equal("gin", index.GetMethod());
        Assert.Equal(["gin_trgm_ops"], index.GetOperators());
    }
}
