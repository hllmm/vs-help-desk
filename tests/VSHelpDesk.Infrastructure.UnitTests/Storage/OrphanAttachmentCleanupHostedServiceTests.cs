using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using VSHelpDesk.Application.Abstractions.Persistence;
using VSHelpDesk.Application.Abstractions.Persistence.Repositories;
using VSHelpDesk.Application.Abstractions.Storage;
using VSHelpDesk.Domain.Entities;
using VSHelpDesk.Infrastructure.Persistence;
using VSHelpDesk.Infrastructure.Persistence.Repositories;
using VSHelpDesk.Infrastructure.Storage;
using Xunit;

namespace VSHelpDesk.Infrastructure.UnitTests.Storage;

public sealed class OrphanAttachmentCleanupHostedServiceTests : IDisposable
{
    private readonly string _tempStorageDir;
    private readonly ApplicationDbContext _dbContext;
    private readonly ServiceProvider _serviceProvider;

    public OrphanAttachmentCleanupHostedServiceTests()
    {
        _tempStorageDir = Path.Combine(Path.GetTempPath(), "VS_OrphanTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempStorageDir);

        var dbName = Guid.NewGuid().ToString();
        var dbOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;

        _dbContext = new ApplicationDbContext(dbOptions);

        var services = new ServiceCollection();
        services.AddDbContext<ApplicationDbContext>(opts => opts.UseInMemoryDatabase(dbName));
        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<ApplicationDbContext>());
        services.AddScoped<ITicketAttachmentRepository, EfTicketAttachmentRepository>();
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();

        var options = Options.Create(new FileStorageOptions { RootPath = _tempStorageDir });
        var hostEnv = new FakeHostEnv { ContentRootPath = _tempStorageDir };
        services.AddSingleton<IFileStorage>(new LocalFileStorage(options, hostEnv, NullLogger<LocalFileStorage>.Instance));

        _serviceProvider = services.BuildServiceProvider();
    }

    [Fact]
    public async Task Cleanup_RemovesPhysicalFile_WhenNoDbRecordExists()
    {
        // Arrange: Create physical file without DB record
        var orphanFileName = "orphan_file_123.pdf";
        var filePath = Path.Combine(_tempStorageDir, orphanFileName);
        await File.WriteAllTextAsync(filePath, "Orphan file content");

        var scopeFactory = _serviceProvider.GetRequiredService<IServiceScopeFactory>();
        var service = new OrphanAttachmentCleanupHostedService(
            scopeFactory,
            NullLogger<OrphanAttachmentCleanupHostedService>.Instance);

        // Act
        await service.CleanupOrphanAttachmentsAsync(CancellationToken.None);

        // Assert
        Assert.False(File.Exists(filePath), "Physical file without DB record should be deleted.");
    }

    [Fact]
    public async Task Cleanup_RemovesDbRecordAndPhysicalFile_WhenParentMessageDoesNotExist()
    {
        // Arrange: Add attachment to DB whose TicketMessageId does not exist in TicketMessages table
        var orphanStoredFile = "orphan_in_db.pdf";
        var filePath = Path.Combine(_tempStorageDir, orphanStoredFile);
        await File.WriteAllTextAsync(filePath, "Orphan DB file content");

        var attachment = new TicketAttachment(
            ticketMessageId: Guid.NewGuid(), // Non-existent message ID
            fileName: "orphan.pdf",
            storedFileName: orphanStoredFile,
            filePath: filePath,
            contentType: "application/pdf",
            fileSize: 100);

        _dbContext.TicketAttachments.Add(attachment);
        await _dbContext.SaveChangesAsync();

        var scopeFactory = _serviceProvider.GetRequiredService<IServiceScopeFactory>();
        var service = new OrphanAttachmentCleanupHostedService(
            scopeFactory,
            NullLogger<OrphanAttachmentCleanupHostedService>.Instance);

        // Act
        await service.CleanupOrphanAttachmentsAsync(CancellationToken.None);

        // Assert
        Assert.False(File.Exists(filePath), "File should be deleted from storage.");
        var remainingDb = await _dbContext.TicketAttachments.FirstOrDefaultAsync(a => a.Id == attachment.Id);
        Assert.Null(remainingDb);
    }

    [Fact]
    public async Task Cleanup_PreservesValidAttachments()
    {
        // Arrange: Valid ticket message and attachment
        var ticket = Ticket.Create("VS-100001", "Test", "Customer", "cust@example.test", DateTime.UtcNow);
        _dbContext.Tickets.Add(ticket);
        await _dbContext.SaveChangesAsync();

        var message = new TicketMessage(ticket.Id, Domain.Enums.MessageSenderType.Customer, "Hello", false);
        _dbContext.TicketMessages.Add(message);
        await _dbContext.SaveChangesAsync();

        var validStoredFile = "valid_file.pdf";
        var filePath = Path.Combine(_tempStorageDir, validStoredFile);
        await File.WriteAllTextAsync(filePath, "Valid file content");

        var attachment = new TicketAttachment(
            ticketMessageId: message.Id,
            fileName: "valid.pdf",
            storedFileName: validStoredFile,
            filePath: filePath,
            contentType: "application/pdf",
            fileSize: 100);

        _dbContext.TicketAttachments.Add(attachment);
        await _dbContext.SaveChangesAsync();

        var scopeFactory = _serviceProvider.GetRequiredService<IServiceScopeFactory>();
        var service = new OrphanAttachmentCleanupHostedService(
            scopeFactory,
            NullLogger<OrphanAttachmentCleanupHostedService>.Instance);

        // Act
        await service.CleanupOrphanAttachmentsAsync(CancellationToken.None);

        // Assert
        Assert.True(File.Exists(filePath), "Valid attachment file should NOT be deleted.");
        var dbRecord = await _dbContext.TicketAttachments.FirstOrDefaultAsync(a => a.Id == attachment.Id);
        Assert.NotNull(dbRecord);
    }

    public void Dispose()
    {
        _serviceProvider.Dispose();
        _dbContext.Dispose();
        if (Directory.Exists(_tempStorageDir))
        {
            try { Directory.Delete(_tempStorageDir, recursive: true); } catch { }
        }
    }

    private sealed class FakeHostEnv : Microsoft.Extensions.Hosting.IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "Test";
        public string ContentRootPath { get; set; } = string.Empty;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }
}
