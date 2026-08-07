using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using VSHelpDesk.Infrastructure.Persistence;
using VSHelpDesk.Infrastructure.Persistence.Migrations;

namespace VSHelpDesk.Infrastructure.UnitTests.Persistence;

public sealed class PortalTicketRequestPersistenceTests
{
    [Fact]
    public void Model_MapsPortalTicketRequestWithUserScopedUniqueKeyAndHashOnly()
    {
        using var context = CreateMetadataContext();

        var entityType = context.Model.FindEntityType(
            "VSHelpDesk.Domain.Entities.PortalTicketRequest");

        Assert.NotNull(entityType);
        Assert.Equal("PortalTicketRequests", entityType!.GetTableName());
        Assert.Equal(
            ["Id"],
            entityType.FindPrimaryKey()!.Properties.Select(property => property.Name));
        Assert.Equal(
            ["UserId", "IdempotencyKey"],
            entityType.GetIndexes()
                .Single(index => index.IsUnique)
                .Properties
                .Select(property => property.Name));
        Assert.Equal(
            "UX_PortalTicketRequests_UserId_IdempotencyKey",
            entityType.GetIndexes().Single(index => index.IsUnique).GetDatabaseName());
        Assert.Equal(36, entityType.FindProperty("IdempotencyKey")!.GetMaxLength());
        Assert.Equal(64, entityType.FindProperty("RequestHash")!.GetMaxLength());
        Assert.False(entityType.FindProperty("RequestHash")!.IsNullable);
        Assert.False(entityType.FindProperty("TicketId")!.IsNullable);
        Assert.False(entityType.FindProperty("CreatedAtUtc")!.IsNullable);
        Assert.Equal(
            "timestamp with time zone",
            entityType.FindProperty("CreatedAtUtc")!.GetColumnType());
        Assert.DoesNotContain(
            entityType.GetProperties(),
            property => property.Name is "Subject" or "CustomerName" or "CustomerEmail" or "Content");
    }

    [Fact]
    public void Migration_CreatesOnlyPortalRequestState()
    {
        var migration = new AddPortalTicketRequestsProbe();

        Assert.Contains(
            migration.CapturedUpOperations,
            operation => operation is CreateTableOperation { Name: "PortalTicketRequests" });
        Assert.Contains(
            migration.CapturedUpOperations,
            operation => operation is CreateIndexOperation index &&
                index.Name == "UX_PortalTicketRequests_UserId_IdempotencyKey" &&
                index.IsUnique);
        Assert.DoesNotContain(
            migration.CapturedUpOperations,
            operation => operation is AddColumnOperation { Name: "xmin" });
        Assert.DoesNotContain(
            migration.CapturedDownOperations,
            operation => operation is DropColumnOperation { Name: "xmin" });
    }

    private static ApplicationDbContext CreateMetadataContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=metadata_test;Username=test_user")
            .Options;
        return new ApplicationDbContext(options);
    }

    private sealed class AddPortalTicketRequestsProbe : AddPortalTicketRequests
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
}
