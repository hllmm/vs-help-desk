using System.Buffers;

namespace VSHelpDesk.Application.Features.Attachments;

public static class BoundedAttachmentContent
{
    public static async Task<byte[]> ReadAsync(
        Stream content,
        long maxBytes,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentOutOfRangeException.ThrowIfNegative(maxBytes);

        var maximumWithSentinel = checked(maxBytes + 1);
        var buffer = ArrayPool<byte>.Shared.Rent(8192);
        try
        {
            using var destination = new MemoryStream();
            long total = 0;

            while (total < maximumWithSentinel)
            {
                var remainingWithSentinel = maximumWithSentinel - total;
                var requested = (int)Math.Min(buffer.Length, remainingWithSentinel);
                var read = await content.ReadAsync(
                    buffer.AsMemory(0, requested),
                    cancellationToken);
                if (read == 0)
                {
                    break;
                }

                await destination.WriteAsync(
                    buffer.AsMemory(0, read),
                    cancellationToken);
                total += read;
                if (total > maxBytes)
                {
                    throw new AttachmentTooLargeException(maxBytes);
                }
            }

            return destination.ToArray();
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}

public sealed class AttachmentTooLargeException(long maxBytes)
    : Exception($"Attachment exceeds the maximum allowed size of {maxBytes} bytes.");
