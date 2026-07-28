using VSHelpDesk.Application.Features.Attachments;

namespace VSHelpDesk.Application.UnitTests.Features.Attachments;

public sealed class BoundedAttachmentContentTests
{
    [Fact]
    public async Task ReadAsync_StopsAtMaximumPlusOne()
    {
        await using var source = new CountingNonSeekableStream(new byte[100]);

        await Assert.ThrowsAsync<AttachmentTooLargeException>(
            () => BoundedAttachmentContent.ReadAsync(source, 10, CancellationToken.None));

        Assert.Equal(11, source.BytesRead);
    }

    [Fact]
    public async Task ReadAsync_ExactMaximum_ReturnsEveryByte()
    {
        byte[] expected = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9];
        await using var source = new CountingNonSeekableStream(expected);

        var actual = await BoundedAttachmentContent.ReadAsync(
            source,
            expected.Length,
            CancellationToken.None);

        Assert.Equal(expected, actual);
        Assert.Equal(expected.Length, source.BytesRead);
    }

    [Fact]
    public async Task ReadAsync_SeekableStream_ReadsFromCurrentPosition()
    {
        await using var source = new MemoryStream("prefix-content"u8.ToArray());
        source.Position = "prefix-"u8.Length;

        var actual = await BoundedAttachmentContent.ReadAsync(
            source,
            maxBytes: 7,
            CancellationToken.None);

        Assert.Equal("content"u8.ToArray(), actual);
        Assert.Equal(source.Length, source.Position);
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

        public override int Read(byte[] buffer, int offset, int count)
        {
            var copied = Math.Min(count, content.Length - position);
            content.AsSpan(position, copied).CopyTo(buffer.AsSpan(offset, copied));
            position += copied;
            BytesRead += copied;
            return copied;
        }

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
}
