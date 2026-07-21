using Microsoft.Extensions.Logging.Abstractions;
using VSHelpDesk.Application.Abstractions.Persistence;
using VSHelpDesk.Application.Abstractions.Storage;
using VSHelpDesk.Application.Features.Attachments;
using VSHelpDesk.Domain.Entities;
using VSHelpDesk.Domain.Enums;

namespace VSHelpDesk.Application.UnitTests.Features.Attachments;

public sealed class TicketAttachmentWriterTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 8, 5, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task TryWrite_AllowedText_StoresMetadataAndFile()
    {
        var message = CreateMessage();
        var db = new FakeDb(message);
        var storage = new RecordingStorage();
        var writer = CreateWriter(db, storage, maxBytes: 1024, allowed: ["text/plain"]);

        await using var content = new MemoryStream("hello"u8.ToArray());
        var result = await writer.TryWriteAsync(
            message.Id,
            "note.txt",
            "text/plain",
            content,
            content.Length,
            CancellationToken.None);

        Assert.True(result.WasStored);
        Assert.NotNull(result.AttachmentId);
        Assert.Single(db.Attachments);
        Assert.Single(storage.Saved);
        Assert.Equal("note.txt", db.Attachments[0].FileName);
    }

    [Fact]
    public async Task TryWrite_DisallowedMime_SkipsWithoutStorageOrDb()
    {
        var message = CreateMessage();
        var db = new FakeDb(message);
        var storage = new RecordingStorage();
        var writer = CreateWriter(db, storage, maxBytes: 1024, allowed: ["text/plain"]);

        await using var content = new MemoryStream("x"u8.ToArray());
        var result = await writer.TryWriteAsync(
            message.Id,
            "evil.exe",
            "application/x-msdownload",
            content,
            content.Length,
            CancellationToken.None);

        Assert.False(result.WasStored);
        Assert.Contains("not allowed", result.SkipReason, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(db.Attachments);
        Assert.Empty(storage.Saved);
    }

    [Fact]
    public async Task TryWrite_ZeroSize_Skips()
    {
        var message = CreateMessage();
        var db = new FakeDb(message);
        var storage = new RecordingStorage();
        var writer = CreateWriter(db, storage, maxBytes: 1024, allowed: ["text/plain"]);

        await using var content = new MemoryStream();
        var result = await writer.TryWriteAsync(
            message.Id,
            "empty.txt",
            "text/plain",
            content,
            declaredSize: 0,
            CancellationToken.None);

        Assert.False(result.WasStored);
        Assert.Empty(db.Attachments);
        Assert.Empty(storage.Saved);
    }

    [Fact]
    public async Task TryWrite_TooLarge_SkipsWithoutStorageOrDb()
    {
        var message = CreateMessage();
        var db = new FakeDb(message);
        var storage = new RecordingStorage();
        var writer = CreateWriter(db, storage, maxBytes: 4, allowed: ["text/plain"]);

        await using var content = new MemoryStream("12345"u8.ToArray());
        var result = await writer.TryWriteAsync(
            message.Id,
            "notes.txt",
            "text/plain",
            content,
            content.Length,
            CancellationToken.None);

        Assert.False(result.WasStored);
        Assert.Contains("maximum", result.SkipReason, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(db.Attachments);
        Assert.Empty(storage.Saved);
    }

    private static TicketMessage CreateMessage()
    {
        var ticket = Ticket.Create("VS-000501", "Attach", "Ada", "ada@t.com", FixedNow.UtcDateTime);
        return new TicketMessage(
            ticket.Id,
            MessageSenderType.Customer,
            "Body",
            createdAtUtc: FixedNow.UtcDateTime);
    }

    private static TicketAttachmentWriter CreateWriter(
        FakeDb db,
        IFileStorage storage,
        long maxBytes,
        string[] allowed) =>
        new(
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
    }

    private sealed class FakeDb : IApplicationDbContext
    {
        private readonly List<TicketMessage> messages;
        private readonly List<object> pending = [];

        public List<TicketAttachment> Attachments { get; } = [];

        public FakeDb(params TicketMessage[] messages) => this.messages = messages.ToList();

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
