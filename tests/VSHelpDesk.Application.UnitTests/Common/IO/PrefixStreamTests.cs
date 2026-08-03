using VSHelpDesk.Application.Common.IO;

namespace VSHelpDesk.Application.UnitTests.Common.IO;

public sealed class PrefixStreamTests
{
    [Fact]
    public void Properties_BehaveAsNonSeekableReadOnlyStream()
    {
        using var remainder = new MemoryStream(new byte[] { 5, 6, 7 });
        using var stream = new PrefixStream(new byte[] { 1, 2, 3, 4 }, remainder);

        Assert.True(stream.CanRead);
        Assert.False(stream.CanSeek);
        Assert.False(stream.CanWrite);
        Assert.Throws<NotSupportedException>(() => stream.Length);
        Assert.Throws<NotSupportedException>(() => stream.Position);
        Assert.Throws<NotSupportedException>(() => stream.Position = 0);
        Assert.Throws<NotSupportedException>(() => stream.Seek(0, SeekOrigin.Begin));
        Assert.Throws<NotSupportedException>(() => stream.SetLength(10));
        Assert.Throws<NotSupportedException>(() => stream.Write(new byte[] { 1 }, 0, 1));
    }

    [Fact]
    public void Read_FillsBufferFromPrefixThenRemainder()
    {
        byte[] prefix = new byte[] { 10, 20, 30 };
        using var remainder = new MemoryStream(new byte[] { 40, 50, 60, 70 });
        using var stream = new PrefixStream(prefix, remainder);

        byte[] buffer = new byte[5];
        int read1 = stream.Read(buffer, 0, buffer.Length);

        Assert.Equal(5, read1);
        Assert.Equal(new byte[] { 10, 20, 30, 40, 50 }, buffer);

        byte[] buffer2 = new byte[5];
        int read2 = stream.Read(buffer2, 0, buffer2.Length);
        Assert.Equal(2, read2);
        Assert.Equal(new byte[] { 60, 70, 0, 0, 0 }, buffer2);

        int readEof = stream.Read(buffer2, 0, buffer2.Length);
        Assert.Equal(0, readEof);
    }

    [Fact]
    public async Task ReadAsync_SingleBufferSpanningPrefixAndRemainder_ReadsAll()
    {
        byte[] prefix = new byte[] { 1, 2 };
        await using var remainder = new MemoryStream(new byte[] { 3, 4, 5 });
        await using var stream = new PrefixStream(prefix, remainder);

        byte[] small = new byte[2];
        int read1 = await stream.ReadAsync(small);
        Assert.Equal(2, read1);
        Assert.Equal(new byte[] { 1, 2 }, small);

        byte[] rest = new byte[10];
        int read2 = await stream.ReadAsync(rest);
        Assert.Equal(3, read2);
        Assert.Equal(new byte[] { 3, 4, 5 }, rest.Take(3));
    }

    [Fact]
    public void Dispose_DisposesRemainderStream()
    {
        var remainder = new NonSeekableTestStream(new byte[] { 100 });
        var stream = new PrefixStream(new byte[] { 1 }, remainder);

        stream.Dispose();

        Assert.True(remainder.IsDisposed);
        Assert.Throws<ObjectDisposedException>(() => stream.Read(new byte[1], 0, 1));
    }

    private sealed class NonSeekableTestStream(byte[] data) : MemoryStream(data)
    {
        public bool IsDisposed { get; private set; }

        public override bool CanSeek => false;

        protected override void Dispose(bool disposing)
        {
            IsDisposed = true;
            base.Dispose(disposing);
        }
    }
}
