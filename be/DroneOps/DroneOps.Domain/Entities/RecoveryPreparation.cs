namespace DroneOps.Domain.Entities;

// Operator-supplied measurements, reviewed with the mission. This declaration
// is not an independent calibration certificate or proof of FC enforcement.
public sealed record RecoveryEnergyProfile(string MeasurementReference, Guid UavId, string BatteryIdentifier,
    string AirframeConfiguration, DateTimeOffset MeasuredAtUtc,
    DateTimeOffset ValidUntilUtc, decimal PayloadKg, decimal MaxWindMetersPerSecond,
    decimal MinTemperatureCelsius, decimal MaxTemperatureCelsius, decimal UsableCapacityWh,
    decimal CruisePowerWatts, decimal ClimbPowerWatts, decimal DescentPowerWatts,
    decimal LandingPowerWatts, decimal HoverPowerWatts, decimal MinimumGroundSpeedMetersPerSecond,
    decimal ReturnAltitudeMeters, decimal LandingDurationSeconds, decimal HoldDurationSeconds,
    decimal EnergyMarginFactor, IReadOnlyList<RemainingEnergyPoint> RemainingEnergyCurve);

// Measured lower bounds, not a linear percent-times-nameplate-capacity estimate.
public sealed record RemainingEnergyPoint(decimal BatteryPercent, decimal LowerBoundWh);

public sealed record LandingSiteSurvey(DateTimeOffset SurveyedAtUtc, DateTimeOffset ValidUntilUtc,
    bool GroundClear, bool ApproachClear, string EvidenceReference);
