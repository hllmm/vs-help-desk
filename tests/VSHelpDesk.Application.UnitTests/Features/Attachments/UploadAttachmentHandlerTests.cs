using Microsoft.Extensions.Logging.Abstractions;
using VSHelpDesk.Application.Abstractions.Persistence;
using VSHelpDesk.Application.Abstractions.Storage;
using VSHelpDesk.Application.Common.Exceptions;
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
        Assert.Contains("not allowed", result.Error, StringComparison.OrdinalIgnoreCase);
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
        Assert.Contains("maximum", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(db.Attachments);
        Assert.Empty(storage.Saved);
    }

    [Fact]
    public async Task Upload_InvalidContent_RejectsBeforeStorage()
    {
        var message = CreateMessage();
        var db = new FakeDb(message);
        var storage = new RecordingStorage();
        var handler = CreateHandler(db, storage, maxBytes: 1024, allowed: ["text/plain"]);

        await using var content = new MemoryStream("invalid"u8.ToArray());
        var result = await handler.HandleAsync(
            new UploadAttachmentCommand(
                message.Id,
                "notes.txt",
                "text/plain",
                content.Length,
                content),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Contains("content", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(storage.Saved);
        Assert.Empty(db.Attachments);
    }

    [Fact]
    public async Task Upload_NonSeekableActualSizeExceedsDeclaredSize_ReadsOnlyMaximumPlusOne()
    {
        var message = CreateMessage();
        var db = new FakeDb(message);
        var storage = new RecordingStorage();
        var handler = CreateHandler(db, storage, maxBytes: 10, allowed: ["text/plain"]);
        await using var content = new CountingNonSeekableStream(new byte[100]);

        var result = await handler.HandleAsync(
            new UploadAttachmentCommand(
                message.Id,
                "notes.txt",
                "text/plain",
                DeclaredFileSize: 10,
                content),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Contains("maximum", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(11, content.BytesRead);
        Assert.Empty(storage.Saved);
        Assert.Empty(db.Attachments);
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
        string[] allowed) =>
        new(
            db,
            storage,
            new FixedPolicy(maxBytes, allowed),
            new FixedTimeProvider(FixedNow),
            NullLogger<UploadAttachmentHandler>.Instance);

    private sealed class FixedPolicy(long maxBytes, string[] allowed) : IAttachmentUploadPolicy
    {
        private readonly HashSet<string> set = new(allowed, StringComparer.OrdinalIgnoreCase);

        public long MaxFileSizeBytes => maxBytes;

        public AttachmentValidationResult Validate(
            string fileName,
            string? declaredContentType,
            ReadOnlySpan<byte> content)
        {
            var declared = declaredContentType?.Split(';')[0].Trim();
            return declared is not null &&
                   set.Contains(declared) &&
                   !content.SequenceEqual("invalid"u8)
                ? AttachmentValidationResult.Allowed(declared)
                : AttachmentValidationResult.Rejected("File content is not allowed.");
        }
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

    private sealed class CountingNonSeekableStream(byte[] content) : Stream
    {
        private int position;

        public int BytesRead { get; private set; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var copied = Math.Min(buffer.Length, content.Length - position);
            content.AsMemory(position, copied).CopyTo(buffer);
            position += copied;
            BytesRead += copied;
            return ValueTask.FromResult(copied);
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) =>
            throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
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
