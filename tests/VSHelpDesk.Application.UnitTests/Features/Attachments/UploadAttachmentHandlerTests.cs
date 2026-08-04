using Microsoft.Extensions.Logging.Abstractions;
using VSHelpDesk.Application.Abstractions.Persistence;
using VSHelpDesk.Application.Abstractions.Persistence.Repositories;
using VSHelpDesk.Application.Abstractions.Storage;
using VSHelpDesk.Application.Common.Exceptions;
using VSHelpDesk.Application.Features.Attachments;
using VSHelpDesk.Application.Features.Attachments.UploadAttachment;
using VSHelpDesk.Domain.Entities;
using VSHelpDesk.Domain.Enums;

namespace VSHelpDesk.Application.UnitTests.Features.Attachments;

public sealed class UploadAttachmentHandlerTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 8, 5, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Upload_AllowedPdf_SavesMetadataAndStorage()
    {
        var message = CreateMessage();
        var db = new FakeDb(message);
        var storage = new RecordingStorage();
        var handler = CreateHandler(db, storage, maxBytes: 1024, allowed: ["application/pdf"]);

        await using var content = new MemoryStream("pdf-bytes"u8.ToArray());
        var result = await handler.HandleAsync(
            new UploadAttachmentCommand(
                message.Id,
                "report.pdf",
                "application/pdf",
                content.Length,
                content),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("report.pdf", result.Value!.FileName);
        Assert.Equal("application/pdf", result.Value.ContentType);
        Assert.Single(db.Attachments);
        Assert.Equal(message.Id, db.Attachments[0].TicketMessageId);
        Assert.Single(storage.Saved);
        Assert.Equal(db.Attachments[0].StoredFileName, storage.Saved[0].StoredFileName);
    }

    [Fact]
    public async Task Upload_DisallowedMime_RejectsWithoutStorageOrDb()
    {
        var message = CreateMessage();
        var db = new FakeDb(message);
        var storage = new RecordingStorage();
        var handler = CreateHandler(db, storage, maxBytes: 1024, allowed: ["application/pdf"]);

        await using var content = new MemoryStream("x"u8.ToArray());
        var result = await handler.HandleAsync(
            new UploadAttachmentCommand(
                message.Id,
                "evil.exe",
                "application/x-msdownload",
                content.Length,
                content),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Contains("izin verilmiyor", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(db.Attachments);
        Assert.Empty(storage.Saved);
    }

    [Fact]
    public async Task Upload_TooLarge_RejectsWithoutStorageOrDb()
    {
        var message = CreateMessage();
        var db = new FakeDb(message);
        var storage = new RecordingStorage();
        var handler = CreateHandler(db, storage, maxBytes: 4, allowed: ["text/plain"]);

        await using var content = new MemoryStream("12345"u8.ToArray());
        var result = await handler.HandleAsync(
            new UploadAttachmentCommand(
                message.Id,
                "notes.txt",
                "text/plain",
                content.Length,
                content),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Contains("azami", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(db.Attachments);
        Assert.Empty(storage.Saved);
    }

    [Fact]
    public async Task Upload_UnknownMessage_ThrowsNotFound()
    {
        var handler = CreateHandler(
            new FakeDb(),
            new RecordingStorage(),
            maxBytes: 1024,
            allowed: ["text/plain"]);

        await using var content = new MemoryStream("x"u8.ToArray());
        await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.HandleAsync(
                new UploadAttachmentCommand(
                    Guid.NewGuid(),
                    "a.txt",
                    "text/plain",
                    content.Length,
                    content),
                CancellationToken.None));
    }

    private static TicketMessage CreateMessage()
    {
        var ticket = Ticket.Create("VS-000401", "Attach", "Ada", "ada@t.com", FixedNow.UtcDateTime);
        return new TicketMessage(
            ticket.Id,
            MessageSenderType.Support,
            "Body",
            createdAtUtc: FixedNow.UtcDateTime);
    }

    private static UploadAttachmentHandler CreateHandler(
        FakeDb db,
        IFileStorage storage,
        long maxBytes,
        string[] allowed)
    {
        var writer = new TicketAttachmentWriter(
            db,
            db,
            db,
            storage,
            new FixedPolicy(maxBytes, allowed),
            new FixedTimeProvider(FixedNow),
            NullLogger<TicketAttachmentWriter>.Instance);

        return new UploadAttachmentHandler(writer, db);
    }

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

        public Task<Ticket?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult<Ticket?>(null);

        public Task<Ticket?> GetByNumberAsync(string ticketNumber, CancellationToken cancellationToken = default) =>
            Task.FromResult<Ticket?>(null);

        public IQueryable<Ticket> GetListQueryable() => Tickets;

        public Task AddAsync(Ticket ticket, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public void Update(Ticket ticket) { }

        public Task AddMessageAsync(TicketMessage message, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<bool> MessageExistsAsync(Guid messageId, CancellationToken cancellationToken = default) =>
            Task.FromResult(messages.Any(m => m.Id == messageId));

        public Task<TicketMessage?> GetMessageByIdAsync(Guid messageId, CancellationToken cancellationToken = default) =>
            Task.FromResult(messages.FirstOrDefault(m => m.Id == messageId));

        public Task<Guid> GetFirstMessageIdAsync(Guid ticketId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Guid.Empty);

        Task<TicketAttachment?> ITicketAttachmentRepository.GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(Attachments.FirstOrDefault(a => a.Id == id));

        public Task<TicketAttachment?> GetByStoredFileNameAsync(string storedFileName, CancellationToken cancellationToken = default) =>
            Task.FromResult(Attachments.FirstOrDefault(a => a.StoredFileName == storedFileName));

        public Task AddAsync(TicketAttachment attachment, CancellationToken cancellationToken = default)
        {
            Add(attachment);
            return Task.CompletedTask;
        }
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
