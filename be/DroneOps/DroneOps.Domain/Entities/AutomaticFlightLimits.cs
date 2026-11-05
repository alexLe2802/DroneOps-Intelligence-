namespace DroneOps.Domain.Entities;

// Operational policy, separate from the manufacturer's aircraft performance envelope.
public sealed record AutomaticFlightLimits(
    decimal TakeoffSpeedMetersPerSecond = 1m,
    decimal ClimbSpeedMetersPerSecond = 1m,
    decimal DescentSpeedMetersPerSecond = 0.7m,
    decimal LandingSpeedMetersPerSecond = 0.7m,
    decimal MaxAltitudeMeters = 30m,
    decimal MaxWindMetersPerSecond = 3m,
    decimal MaxGustMetersPerSecond = 5m,
    string PolicyVersion = "multicopter-envelope-v1",
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    RecoveryEnergyProfile? RecoveryEnergyProfile = null);
