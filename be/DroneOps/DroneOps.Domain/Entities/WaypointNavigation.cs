namespace DroneOps.Domain.Entities;

public sealed record WaypointNavigation(
    decimal? SpeedMetersPerSecond = null,
    decimal? HoldTimeSeconds = null,
    decimal? AcceptanceRadiusMeters = null,
    decimal? PassRadiusMeters = null,
    decimal? YawDegrees = null,
    bool EmergencyLandingCandidate = false,
    decimal? LandingGroundAltitudeMeters = null,
    string? LandingSiteNotes = null);

public sealed record FirmwareParameterSnapshot(string FirmwareVersion, Dictionary<string, decimal> Values,
    DateTimeOffset ObservedAtUtc, string? HardwareIdentity = null, string Source = "FC MAVLink");
