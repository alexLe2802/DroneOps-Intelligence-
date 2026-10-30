namespace DroneOps.API;

public static class LiveStreamDiagnostics
{
    private static long _activeSubscribers;
    private static long _openedSubscribers;
    private static long _closedSubscribers;

    public static void Opened()
    {
        Interlocked.Increment(ref _activeSubscribers);
        Interlocked.Increment(ref _openedSubscribers);
    }

    public static void Closed()
    {
        Interlocked.Decrement(ref _activeSubscribers);
        Interlocked.Increment(ref _closedSubscribers);
    }

    public static object Snapshot() => new
    {
        capturedAt = DateTimeOffset.UtcNow,
        activeSubscribers = Interlocked.Read(ref _activeSubscribers),
        openedSubscribers = Interlocked.Read(ref _openedSubscribers),
        closedSubscribers = Interlocked.Read(ref _closedSubscribers),
        managedMemoryBytes = GC.GetTotalMemory(forceFullCollection: false),
        pendingThreadPoolWorkItems = ThreadPool.PendingWorkItemCount
    };
}
