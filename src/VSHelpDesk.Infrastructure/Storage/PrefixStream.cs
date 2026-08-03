namespace VSHelpDesk.Infrastructure.Storage;

/// <summary>
/// A non-seekable read-only stream that yields a prefix byte buffer first,
/// followed by the remaining contents of an underlying stream.
/// Prevents allocating full MemoryStream buffers in memory when sniffing headers.
/// </summary>
public sealed class PrefixStream : Stream
{
    private readonly ReadOnlyMemory<byte> _prefix;
    private readonly Stream _remainder;
    private int _prefixOffset;
    private bool _disposed;

    public PrefixStream(ReadOnlyMemory<byte> prefix, Stream remainder)
    {
        _prefix = prefix;
        _remainder = remainder ?? throw new ArgumentNullException(nameof(remainder));
    }

    public override bool CanRead => !_disposed && _remainder.CanRead;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException("PrefixStream does not support Length.");

    public override long Position
    {
        get => throw new NotSupportedException("PrefixStream does not support Position.");
        set => throw new NotSupportedException("PrefixStream does not support Position.");
    }

    public override void Flush() => _remainder.Flush();

    public override Task FlushAsync(CancellationToken cancellationToken) => _remainder.FlushAsync(cancellationToken);

    public override int Read(byte[] buffer, int offset, int count)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        return Read(buffer.AsSpan(offset, count));
    }

    public override int Read(Span<byte> buffer)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        int totalRead = 0;
        int remainingPrefix = _prefix.Length - _prefixOffset;

        if (remainingPrefix > 0)
        {
            int toCopy = Math.Min(buffer.Length, remainingPrefix);
            _prefix.Span.Slice(_prefixOffset, toCopy).CopyTo(buffer);
            _prefixOffset += toCopy;
            totalRead += toCopy;

            if (totalRead == buffer.Length)
            {
                return totalRead;
            }

            buffer = buffer.Slice(toCopy);
        }

        int remainderRead = _remainder.Read(buffer);
        return totalRead + remainderRead;
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        int totalRead = 0;
        int remainingPrefix = _prefix.Length - _prefixOffset;

        if (remainingPrefix > 0)
        {
            int toCopy = Math.Min(buffer.Length, remainingPrefix);
            _prefix.Span.Slice(_prefixOffset, toCopy).CopyTo(buffer.Span);
            _prefixOffset += toCopy;
            totalRead += toCopy;

            if (totalRead == buffer.Length)
            {
                return totalRead;
            }

            buffer = buffer.Slice(toCopy);
        }

        int remainderRead = await _remainder.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
        return totalRead + remainderRead;
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        return ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
    }

    public override long Seek(long offset, SeekOrigin origin) =>
        throw new NotSupportedException("PrefixStream does not support Seek.");

    public override void SetLength(long value) =>
        throw new NotSupportedException("PrefixStream does not support SetLength.");

    public override void Write(byte[] buffer, int offset, int count) =>
        throw new NotSupportedException("PrefixStream is read-only.");

    protected override void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                _remainder.Dispose();
            }
            _disposed = true;
        }
        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            _disposed = true;
            await _remainder.DisposeAsync().ConfigureAwait(false);
        }
        await base.DisposeAsync().ConfigureAwait(false);
    }
}
