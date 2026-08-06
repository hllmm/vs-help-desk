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
        public int SaveCallCount { get; private set; }

        public async Task<StoredFile> SaveAsync(
            Stream content,
            string originalFileName,
            string contentType,
            CancellationToken cancellationToken)
        {
            SaveCallCount++;
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

        public Task<Stream> OpenReadAsync(string storedFileName, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task DeleteAsync(string storedFileName, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<string>> ListStoredFilesAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<string>>(Saved.Select(s => s.StoredFileName).ToList());
    }

    [Fact]
    public async Task NonSeekableStream_FalseSmallDeclaredSize_StillEnforcesMaxPlusOne_AndDoesNotSave()
    {
        var message = new TicketMessage(Guid.NewGuid(), Domain.Enums.MessageSenderType.Customer, "Body");
        var db = new FakeDb(message);
        var storage = new RecordingStorage();
        var maxBytes = 10L;
        var writer = CreateWriter(db, storage, maxBytes: maxBytes, allowed: ["image/png"]);
        // declared size lies small, actual payload is exactly max+1 =11 bytes
        var payload = new byte[maxBytes + 1];
        for (var i = 0; i < payload.Length; i++) payload[i] = (byte)(i % 256);
        var counted = new CountingReadStream(new MemoryStream(payload), seekable: false);

        var factory = new RecordingTempFactory();
        var writerWithFactory = new TicketAttachmentWriter(
            db,
            db,
            db,
            storage,
            new FixedPolicy(maxBytes, ["image/png"]),
            new FixedTimeProvider(FixedNow),
            NullLogger<TicketAttachmentWriter>.Instance,
            temporaryFileFactory: factory);

        var result = await writerWithFactory.TryWriteAsync(
            message.Id,
            "photo.png",
            "image/png",
            counted,
            declaredSize: 1, // false small
            CancellationToken.None);

        Assert.False(result.WasStored);
        Assert.Contains("boyutunu aşıyor", result.SkipReason, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(storage.Saved);
        Assert.Empty(db.Attachments);
        Assert.Equal(0, storage.SaveCallCount);
        Assert.Equal(0, db.AddCallCount);
        Assert.Equal(0, db.SaveCallCount);
        // Must have read exactly max+1 bytes (11) not more, not 4096
        Assert.Equal(maxBytes + 1, counted.TotalBytesRead);
        // temp path deleted after rejection
        Assert.False(string.IsNullOrWhiteSpace(factory.LastCreatedPath));
        Assert.False(File.Exists(factory.LastCreatedPath!));
    }

    [Fact]
    public async Task NonSeekableStream_ExactlyMaxBytes_Succeeds_AndReadsExactlyMax()
    {
        var message = new TicketMessage(Guid.NewGuid(), Domain.Enums.MessageSenderType.Customer, "Body");
        var db = new FakeDb(message);
        var storage = new RecordingStorage();
        var maxBytes = 10L;
        var payload = new byte[maxBytes];
        for (var i = 0; i < payload.Length; i++) payload[i] = (byte)(i % 256);

        var factory = new RecordingTempFactory();
        var writer = new TicketAttachmentWriter(
            db,
            db,
            db,
            storage,
            new FixedPolicy(maxBytes, ["image/png"]),
            new FixedTimeProvider(FixedNow),
            NullLogger<TicketAttachmentWriter>.Instance,
            temporaryFileFactory: factory);

        var counted = new CountingReadStream(new MemoryStream(payload), seekable: false);

        var result = await writer.TryWriteAsync(
            message.Id,
            "photo.png",
            "image/png",
            counted,
            declaredSize: payload.Length,
            CancellationToken.None);

        Assert.True(result.WasStored);
        Assert.Equal(1, storage.SaveCallCount);
        Assert.Equal(1, db.AddCallCount);
        Assert.Equal(1, db.SaveCallCount);
        Assert.Equal(maxBytes, counted.TotalBytesRead);
        Assert.Equal(maxBytes, storage.Saved.Single().FileSize);
        // temp path deleted after success
        Assert.NotNull(factory.LastCreatedPath);
        Assert.False(File.Exists(factory.LastCreatedPath!));
    }

    [Fact]
    public async Task NonSeekableStream_InitialReadLimitedToMin4096AndMaxPlusOne()
    {
        var message = new TicketMessage(Guid.NewGuid(), Domain.Enums.MessageSenderType.Customer, "Body");
        var db = new FakeDb(message);
        var storage = new RecordingStorage();
        var maxBytes = 10L; // small max, initial limit = 11
        var payload = new byte[100]; // much larger than max
        var counted = new CountingReadStream(new MemoryStream(payload), seekable: false);

        var factory = new RecordingTempFactory();
        var writer = new TicketAttachmentWriter(
            db,
            db,
            db,
            storage,
            new FixedPolicy(maxBytes, ["image/png"]),
            new FixedTimeProvider(FixedNow),
            NullLogger<TicketAttachmentWriter>.Instance,
            temporaryFileFactory: factory);

        var result = await writer.TryWriteAsync(
            message.Id,
            "photo.png",
            "image/png",
            counted,
            declaredSize: 1, // false small to force reading
            CancellationToken.None);

        Assert.False(result.WasStored);
        // Must not read more than max+1=11 bytes total
        Assert.Equal(11, counted.TotalBytesRead);
        Assert.Equal(0, storage.SaveCallCount);
    }

    [Fact]
    public async Task SaveAsync_NotCalled_OnRejection_DueToOversize()
    {
        var message = new TicketMessage(Guid.NewGuid(), Domain.Enums.MessageSenderType.Customer, "Body");
        var db = new FakeDb(message);
        var storage = new RecordingStorage();
        var maxBytes = 5L;
        var writer = CreateWriterWithFactory(db, storage, maxBytes, factory: new RecordingTempFactory());
        var payload = new byte[maxBytes + 1];
        using var stream = new MemoryStream(payload);

        var result = await writer.TryWriteAsync(message.Id, "test.png", "image/png", stream, declaredSize: payload.Length, CancellationToken.None);

        Assert.False(result.WasStored);
        Assert.Equal(0, storage.SaveCallCount);
        Assert.Equal(0, db.AddCallCount);
        Assert.Equal(0, db.SaveCallCount);
    }

    [Fact]
    public async Task Cancellation_DuringRead_Propagates_AndDoesNotCallSaveOrRepository()
    {
        var message = new TicketMessage(Guid.NewGuid(), Domain.Enums.MessageSenderType.Customer, "Body");
        var db = new FakeDb(message);
        var storage = new RecordingStorage();
        var factory = new RecordingTempFactory();
        var writer = new TicketAttachmentWriter(
            db,
            db,
            db,
            storage,
            new FixedPolicy(1000, ["image/png"]),
            new FixedTimeProvider(FixedNow),
            NullLogger<TicketAttachmentWriter>.Instance,
            temporaryFileFactory: factory);

        var cts = new CancellationTokenSource();
        cts.Cancel(); // already cancelled

        var payload = new byte[100];
        using var stream = new MemoryStream(payload);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            writer.TryWriteAsync(message.Id, "test.png", "image/png", stream, declaredSize: payload.Length, cancellationToken: cts.Token));

        Assert.Equal(0, storage.SaveCallCount);
        Assert.Equal(0, db.AddCallCount);
        Assert.Equal(0, db.SaveCallCount);
        // temp path must be deleted after cancellation (if created)
        if (factory.LastCreatedPath is not null)
        {
            Assert.False(File.Exists(factory.LastCreatedPath));
        }
    }

    [Fact]
    public async Task TempFile_DeletedAfterSuccess()
    {
        var message = new TicketMessage(Guid.NewGuid(), Domain.Enums.MessageSenderType.Customer, "Body");
        var db = new FakeDb(message);
        var storage = new RecordingStorage();
        var factory = new RecordingTempFactory();
        var writer = new TicketAttachmentWriter(
            db,
            db,
            db,
            storage,
            new FixedPolicy(1000, ["image/png"]),
            new FixedTimeProvider(FixedNow),
            NullLogger<TicketAttachmentWriter>.Instance,
            temporaryFileFactory: factory);

        var payload = "hello"u8.ToArray();
        using var stream = new MemoryStream(payload);

        var result = await writer.TryWriteAsync(message.Id, "a.png", "image/png", stream, declaredSize: payload.Length, CancellationToken.None);

        Assert.True(result.WasStored);
        Assert.NotNull(factory.LastCreatedPath);
        Assert.False(File.Exists(factory.LastCreatedPath!));
    }

    [Fact]
    public async Task TempFile_DeletedAfterRejection()
    {
        var message = new TicketMessage(Guid.NewGuid(), Domain.Enums.MessageSenderType.Customer, "Body");
        var db = new FakeDb(message);
        var storage = new RecordingStorage();
        var factory = new RecordingTempFactory();
        var writer = new TicketAttachmentWriter(
            db,
            db,
            db,
            storage,
            new FixedPolicy(5, ["image/png"]),
            new FixedTimeProvider(FixedNow),
            NullLogger<TicketAttachmentWriter>.Instance,
            temporaryFileFactory: factory);

        var payload = new byte[10];
        var counted = new CountingReadStream(new MemoryStream(payload), seekable: false);

        var result = await writer.TryWriteAsync(message.Id, "a.png", "image/png", counted, declaredSize: 1, CancellationToken.None); // false small to force spool

        Assert.False(result.WasStored);
        Assert.NotNull(factory.LastCreatedPath);
        Assert.False(File.Exists(factory.LastCreatedPath!));
    }

    [Fact]
    public async Task TempFile_DeletedAfterCancellation()
    {
        var message = new TicketMessage(Guid.NewGuid(), Domain.Enums.MessageSenderType.Customer, "Body");
        var db = new FakeDb(message);
        var storage = new RecordingStorage();
        var factory = new RecordingTempFactory();
        var writer = new TicketAttachmentWriter(
            db,
            db,
            db,
            storage,
            new FixedPolicy(1000, ["image/png"]),
            new FixedTimeProvider(FixedNow),
            NullLogger<TicketAttachmentWriter>.Instance,
            temporaryFileFactory: factory);

        var payload = new byte[100];
        var cancelStream = new CancelAfterHeaderStream(new MemoryStream(payload));
        var cts = new CancellationTokenSource();

        // Cancel after header read: stream will throw OCE on second read
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            writer.TryWriteAsync(message.Id, "a.png", "image/png", cancelStream, declaredSize: payload.Length, cancellationToken: cts.Token));

        Assert.Equal(0, storage.SaveCallCount);
        if (factory.LastCreatedPath is not null)
        {
            Assert.False(File.Exists(factory.LastCreatedPath));
        }
    }

    [Fact]
    public async Task ExactlyMaxPlusOneBytes_Read_Guarantee_NotExceeded()
    {
        var message = new TicketMessage(Guid.NewGuid(), Domain.Enums.MessageSenderType.Customer, "Body");
        var db = new FakeDb(message);
        var storage = new RecordingStorage();
        var maxBytes = 4096L;
        var factory = new RecordingTempFactory();
        var writer = new TicketAttachmentWriter(
            db,
            db,
            db,
            storage,
            new FixedPolicy(maxBytes, ["image/png"]),
            new FixedTimeProvider(FixedNow),
            NullLogger<TicketAttachmentWriter>.Instance,
            temporaryFileFactory: factory);

        // Payload exactly max+1 = 4097, declared small to bypass early declaredSize check, non-seekable to force Max+1 read path
        var payload = new byte[maxBytes + 1];
        var counted = new CountingReadStream(new MemoryStream(payload), seekable: false);

        var result = await writer.TryWriteAsync(message.Id, "a.png", "image/png", counted, declaredSize: 1, CancellationToken.None);

        Assert.False(result.WasStored);
        Assert.Equal(maxBytes + 1, counted.TotalBytesRead);
        Assert.Equal(0, storage.SaveCallCount);
    }

    private static TicketAttachmentWriter CreateWriterWithFactory(
        FakeDb db,
        RecordingStorage storage,
        long maxBytes,
        RecordingTempFactory factory) =>
        new(
            db,
            db,
            db,
            storage,
            new FixedPolicy(maxBytes, ["image/png"]),
            new FixedTimeProvider(FixedNow),
            NullLogger<TicketAttachmentWriter>.Instance,
            temporaryFileFactory: factory);

    private sealed class CountingReadStream : Stream
    {
        private readonly Stream inner;
        private readonly bool seekable;
        public long TotalBytesRead { get; private set; }

        public CountingReadStream(Stream inner, bool seekable)
        {
            this.inner = inner;
            this.seekable = seekable;
        }

        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => seekable && inner.CanSeek;
        public override bool CanWrite => false;
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set => inner.Position = value; }
        public override void Flush() => inner.Flush();
        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = inner.Read(buffer, offset, count);
            TotalBytesRead += read;
            return read;
        }

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            var read = await inner.ReadAsync(buffer.AsMemory(offset, count), cancellationToken);
            TotalBytesRead += read;
            return read;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken)
        {
            var read = await inner.ReadAsync(buffer, cancellationToken);
            TotalBytesRead += read;
            return read;
        }

        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
        public override void SetLength(long value) => inner.SetLength(value);
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class CancelAfterHeaderStream : Stream
    {
        private readonly Stream inner;
        private bool firstRead = true;

        public CancelAfterHeaderStream(Stream inner) => this.inner = inner;

        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => inner.CanSeek;
        public override bool CanWrite => false;
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set => inner.Position = value; }
        public override void Flush() => inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken)
        {
            if (!firstRead)
            {
                throw new OperationCanceledException();
            }
            firstRead = false;
            return await inner.ReadAsync(buffer, cancellationToken);
        }

        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
        public override void SetLength(long value) => inner.SetLength(value);
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class RecordingTempFactory : VSHelpDesk.Application.Common.IO.ITemporaryFileFactory
    {
        public string? LastCreatedPath { get; private set; }

        public (FileStream Stream, string Path) CreateTempFile()
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), System.IO.Path.GetRandomFileName());
            var fs = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 8192, FileOptions.Asynchronous);
            LastCreatedPath = path;
            return (fs, path);
        }
    }

    private sealed class FakeDb : IApplicationDbContext, ITicketRepository, ITicketAttachmentRepository, IUnitOfWork
    {
        private readonly List<TicketMessage> messages;
        private readonly List<object> pending = [];

        public List<TicketAttachment> Attachments { get; } = [];
        public int AddCallCount { get; private set; }
        public int SaveCallCount { get; private set; }

        public FakeDb(params TicketMessage[] messages) => this.messages = messages.ToList();

        public Task<Ticket?> GetByIdAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult<Ticket?>(null);
        public Task<Ticket?> GetByNumberAsync(string ticketNumber, CancellationToken cancellationToken) => Task.FromResult<Ticket?>(null);
        public IQueryable<Ticket> GetListQueryable() => Tickets;
        public Task AddAsync(Ticket ticket, CancellationToken cancellationToken) => Task.CompletedTask;
        public void Update(Ticket ticket) { }
        public Task AddMessageAsync(TicketMessage message, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<bool> MessageExistsAsync(Guid messageId, CancellationToken cancellationToken) => Task.FromResult(messages.Any(m => m.Id == messageId));
        public Task<TicketMessage?> GetMessageByIdAsync(Guid messageId, CancellationToken cancellationToken) => Task.FromResult(messages.FirstOrDefault(m => m.Id == messageId));
        public Task<Guid> GetFirstMessageIdAsync(Guid ticketId, CancellationToken cancellationToken) => Task.FromResult(Guid.Empty);

        Task<TicketAttachment?> ITicketAttachmentRepository.GetByIdAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(Attachments.FirstOrDefault(a => a.Id == id));
        public Task<TicketAttachment?> GetByStoredFileNameAsync(string storedFileName, CancellationToken cancellationToken) => Task.FromResult(Attachments.FirstOrDefault(a => a.StoredFileName == storedFileName));
        public Task AddAsync(TicketAttachment attachment, CancellationToken cancellationToken) { AddCallCount++; Add(attachment); return Task.CompletedTask; }
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

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken)
        {
            SaveCallCount++;
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
