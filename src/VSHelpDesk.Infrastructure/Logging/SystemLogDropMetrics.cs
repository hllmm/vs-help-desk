
namespace VSHelpDesk.Infrastructure.Logging;

public sealed class SystemLogDropMetrics
{
    private long _droppedCount;
    public long DroppedCount => Interlocked.Read(ref _droppedCount);
    public void IncrementDroppedCount() => IncrementDroppedCount(1);
    public void IncrementDroppedCount(long count)
    {
        if (count > 0) Interlocked.Add(ref _droppedCount, count);
    }
    public void Reset() => Interlocked.Exchange(ref _droppedCount, 0);
}
