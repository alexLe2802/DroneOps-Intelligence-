namespace DroneOps.Domain.Entities;

public sealed record WaypointNavigation(
    decimal? SpeedMetersPerSecond = null,
    decimal? HoldTimeSeconds = null,
    decimal? AcceptanceRadiusMeters = null,
    decimal? PassRadiusMeters = null,
    decimal? YawDegrees = null,
    bool EmergencyLandingCandidate = false,
    decimal? LandingGroundAltitudeMeters = null,
    string? LandingSiteNotes = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    LandingSiteSurvey? LandingSiteSurvey = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    PlannedHome? PlannedHome = null);

// Plan metadata stored on the first waypoint's existing navigation JSON.
// It is not a command to relocate the vehicle's actual HOME_POSITION.
public sealed record PlannedHome(decimal Latitude, decimal Longitude, decimal? AltitudeMslMeters = null);

public sealed record FirmwareParameterSnapshot(string FirmwareVersion, Dictionary<string, decimal> Values,
    DateTimeOffset ObservedAtUtc, string? HardwareIdentity = null, string Source = "FC MAVLink");
