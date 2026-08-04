using Microsoft.Extensions.Logging.Abstractions;
using VSHelpDesk.Application.Abstractions.Persistence;
using VSHelpDesk.Application.Abstractions.Persistence.Repositories;
using VSHelpDesk.Application.Abstractions.Storage;
using VSHelpDesk.Application.Features.Attachments;
using VSHelpDesk.Domain.Entities;

namespace VSHelpDesk.Application.UnitTests.Features.Attachments;

public sealed class TicketAttachmentWriterTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 7, 28, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task MessageNotFound_ReturnsSkippedResultWithoutSavingFile()
    {
        var db = new FakeDb();
        var storage = new RecordingStorage();
        var writer = CreateWriter(db, storage, maxBytes: 100, allowed: ["image/png"]);
        using var stream = new MemoryStream("data"u8.ToArray());

        var result = await writer.TryWriteAsync(
            Guid.NewGuid(),
            "test.png",
            "image/png",
            stream,
            declaredSize: 4,
            CancellationToken.None);

        Assert.False(result.WasStored);
        Assert.Contains("bulunamadı", result.SkipReason, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(storage.Saved);
        Assert.Empty(db.Attachments);
    }

    [Fact]
    public async Task PolicyViolation_ReturnsSkippedResultWithoutSavingFile()
    {
        var message = new TicketMessage(Guid.NewGuid(), Domain.Enums.MessageSenderType.Customer, "Body");
        var db = new FakeDb(message);
        var storage = new RecordingStorage();
        var writer = CreateWriter(db, storage, maxBytes: 10, allowed: ["image/png"]);
        using var stream = new MemoryStream("very long content exceeding size limit"u8.ToArray());

        var result = await writer.TryWriteAsync(
            message.Id,
            "test.png",
            "image/png",
            stream,
            declaredSize: 100,
            CancellationToken.None);

        Assert.False(result.WasStored);
        Assert.Contains("boyutunu aşıyor", result.SkipReason, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(storage.Saved);
        Assert.Empty(db.Attachments);
    }

    [Fact]
    public async Task ValidAttachment_StoresFileAndPersistsMetadata()
    {
        var message = new TicketMessage(Guid.NewGuid(), Domain.Enums.MessageSenderType.Customer, "Body");
        var db = new FakeDb(message);
        var storage = new RecordingStorage();
        var writer = CreateWriter(db, storage, maxBytes: 1000, allowed: ["image/png"]);
        var payload = "png-content"u8.ToArray();
        using var stream = new MemoryStream(payload);

        var result = await writer.TryWriteAsync(
            message.Id,
            "photo.png",
            "image/png",
            stream,
            declaredSize: payload.Length,
            CancellationToken.None);

        Assert.True(result.WasStored);
        Assert.NotEqual(Guid.Empty, result.AttachmentId);

        var savedFile = Assert.Single(storage.Saved);
        Assert.Equal("image/png", savedFile.ContentType);
        Assert.Equal(payload.Length, savedFile.FileSize);

        var attachment = Assert.Single(db.Attachments);
        Assert.Equal(result.AttachmentId, attachment.Id);
        Assert.Equal(message.Id, attachment.TicketMessageId);
        Assert.Equal("photo.png", attachment.FileName);
        Assert.Equal(savedFile.StoredFileName, attachment.StoredFileName);
        Assert.Equal(savedFile.FilePath, attachment.FilePath);
        Assert.Equal("image/png", attachment.ContentType);
        Assert.Equal(payload.Length, attachment.FileSize);
        Assert.Equal(FixedNow.UtcDateTime, attachment.CreatedAt);
    }

    private static TicketAttachmentWriter CreateWriter(
        FakeDb db,
        IFileStorage storage,
        long maxBytes,
        string[] allowed) =>
        new(
            db,
            db,
            db,
            storage,
            new FixedPolicy(maxBytes, allowed),
            new FixedTimeProvider(FixedNow),
            NullLogger<TicketAttachmentWriter>.Instance);

    private sealed class FixedPolicy(long maxBytes, string[] allowed) : IAttachmentUploadPolicy
    {
        private readonly HashSet<string> set = new(allowed, StringComparer.OrdinalIgnoreCase);

        public long MaxFileSizeBytes => maxBytes;

        public bool IsContentTypeAllowed(string? contentType) =>
            !string.IsNullOrWhiteSpace(contentType) && set.Contains(contentType.Split(';')[0].Trim());

        public string? DetectContentTypeFromContent(ReadOnlySpan<byte> header) => null;

        public bool IsDeclaredTypeConsistentWithContent(
            string? declaredContentType,
            ReadOnlySpan<byte> header) =>
            IsContentTypeAllowed(declaredContentType);
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class RecordingStorage : IFileStorage
    {
        public List<StoredFile> Saved { get; } = [];

        public async Task<StoredFile> SaveAsync(
            Stream content,
            string originalFileName,
            string contentType,
            CancellationToken cancellationToken = default)
        {
            using var ms = new MemoryStream();
            await content.CopyToAsync(ms, cancellationToken);
            var stored = new StoredFile(
                $"{Guid.NewGuid():N}{Path.GetExtension(originalFileName)}",
                $"/tmp/{originalFileName}",
                contentType,
                ms.Length);
            Saved.Add(stored);
            return stored;
        }

        public Task<Stream> OpenReadAsync(string storedFileName, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task DeleteAsync(string storedFileName, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<string>> ListStoredFilesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<string>>(Saved.Select(s => s.StoredFileName).ToList());
    }

    private sealed class FakeDb : IApplicationDbContext, ITicketRepository, ITicketAttachmentRepository, IUnitOfWork
    {
        private readonly List<TicketMessage> messages;
        private readonly List<object> pending = [];

        public List<TicketAttachment> Attachments { get; } = [];

        public FakeDb(params TicketMessage[] messages) => this.messages = messages.ToList();

        public Task<Ticket?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult<Ticket?>(null);
        public Task<Ticket?> GetByNumberAsync(string ticketNumber, CancellationToken cancellationToken = default) => Task.FromResult<Ticket?>(null);
        public IQueryable<Ticket> GetListQueryable() => Tickets;
        public Task AddAsync(Ticket ticket, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Update(Ticket ticket) { }
        public Task AddMessageAsync(TicketMessage message, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<bool> MessageExistsAsync(Guid messageId, CancellationToken cancellationToken = default) => Task.FromResult(messages.Any(m => m.Id == messageId));
        public Task<TicketMessage?> GetMessageByIdAsync(Guid messageId, CancellationToken cancellationToken = default) => Task.FromResult(messages.FirstOrDefault(m => m.Id == messageId));
        public Task<Guid> GetFirstMessageIdAsync(Guid ticketId, CancellationToken cancellationToken = default) => Task.FromResult(Guid.Empty);

        Task<TicketAttachment?> ITicketAttachmentRepository.GetByIdAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult(Attachments.FirstOrDefault(a => a.Id == id));
        public Task<TicketAttachment?> GetByStoredFileNameAsync(string storedFileName, CancellationToken cancellationToken = default) => Task.FromResult(Attachments.FirstOrDefault(a => a.StoredFileName == storedFileName));
        public Task AddAsync(TicketAttachment attachment, CancellationToken cancellationToken = default) { Add(attachment); return Task.CompletedTask; }
        public void Remove(TicketAttachment attachment) => Attachments.Remove(attachment);
        public IQueryable<TicketAttachment> GetOrphansQueryable() => TicketAttachments.Where(a => !messages.Any(m => m.Id == a.TicketMessageId));

        public IQueryable<User> Users => Array.Empty<User>().AsQueryable();
        public IQueryable<Ticket> Tickets => Array.Empty<Ticket>().AsQueryable();
        public IQueryable<TicketMessage> TicketMessages => messages.AsQueryable();
        public IQueryable<TicketAttachment> TicketAttachments => Attachments.AsQueryable();
        public IQueryable<ProcessedEmailMessage> ProcessedEmailMessages =>
            Array.Empty<ProcessedEmailMessage>().AsQueryable();

        public IQueryable<ApplicationParameter> ApplicationParameters =>
            Array.Empty<ApplicationParameter>().AsQueryable();

        public IQueryable<ParameterChangeLog> ParameterChangeLogs =>
            Array.Empty<ParameterChangeLog>().AsQueryable();

        public IQueryable<SystemLog> SystemLogs =>
            Array.Empty<SystemLog>().AsQueryable();

        public void Add<TEntity>(TEntity entity) where TEntity : class => pending.Add(entity!);

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            foreach (var entity in pending)
            {
                if (entity is TicketAttachment attachment)
                {
                    Attachments.Add(attachment);
                }
            }

            pending.Clear();
            return Task.FromResult(1);
        }

        public void ClearTrackedChanges() => pending.Clear();
    }
}
