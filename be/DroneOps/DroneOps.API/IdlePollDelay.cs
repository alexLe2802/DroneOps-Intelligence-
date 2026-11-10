namespace DroneOps.API;

// Empty queues back off; productive cycles immediately restore the normal interval.
public sealed class IdlePollDelay(TimeSpan active, TimeSpan maximum)
{
    private int emptyCycles;
    public TimeSpan Next(bool hadWork)
    {
        emptyCycles = hadWork ? 0 : Math.Min(emptyCycles + 1, 10);
        return TimeSpan.FromMilliseconds(Math.Max(active.TotalMilliseconds,
            Math.Min(maximum.TotalMilliseconds, active.TotalMilliseconds * Math.Pow(2, emptyCycles))));
    }
}
