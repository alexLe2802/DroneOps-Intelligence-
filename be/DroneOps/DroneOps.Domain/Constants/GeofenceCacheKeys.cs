namespace DroneOps.Domain.Constants;

public static class GeofenceCacheKeys
{
    public const string Snapshot = "geofences:runtime-snapshot:v1";
    public static readonly TimeSpan SnapshotLifetime = TimeSpan.FromHours(1);
}
