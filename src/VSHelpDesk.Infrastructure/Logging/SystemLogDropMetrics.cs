namespace VSHelpDesk.Infrastructure.Logging;

/// <summary>
/// Thread-safe metrics tracker for system log entries dropped due to channel capacity limits.
/// </summary>
public sealed class SystemLogDropMetrics
{
    private long _droppedCount;

    public long DroppedCount => Interlocked.Read(ref _droppedCount);

    public void IncrementDroppedCount() => Interlocked.Increment(ref _droppedCount);

    public void Reset() => Interlocked.Exchange(ref _droppedCount, 0);
}
