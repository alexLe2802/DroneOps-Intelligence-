using DroneOps.Domain.Entities;

namespace DroneOps.Application.Services.Missions;

public sealed record RecoveryPosition(decimal Latitude, decimal Longitude, decimal RelativeAltitudeMeters);
public sealed record RecoveryEnergyEstimate(string SiteId, decimal? DistanceMeters, decimal? RequiredEnergyWh,
    bool? FitsDeclaredEnergyBudget, bool SurveyDeclaredCurrent, string[] MissingData);
public sealed record RecoveryEnergyAssessment(string Status, decimal? AvailableEnergyWh, decimal? ReserveEnergyWh,
    IReadOnlyList<RecoveryEnergyEstimate> Sites, IReadOnlyList<string> MissingData,
    bool CanAuthorizeRecovery = false, string Source = "Pilot-declared measurements; manager-reviewed plan; FC verification pending");

public static class RecoveryEnergyCalculator
{
    public static IReadOnlyList<string> ProfileErrors(RecoveryEnergyProfile? p, AutomaticFlightLimits limits,
        DateTimeOffset now, DateTimeOffset? through = null, Guid? expectedUav = null)
    {
        if (p == null) return [];
        var errors = new List<string>();
        if (p.UavId == Guid.Empty || expectedUav.HasValue && p.UavId != expectedUav
            || string.IsNullOrWhiteSpace(p.BatteryIdentifier) || p.BatteryIdentifier.Length is < 3 or > 128
            || string.IsNullOrWhiteSpace(p.AirframeConfiguration) || p.AirframeConfiguration.Length is < 5 or > 500)
            errors.Add("Measurements must identify the matching UAV, installed battery and airframe configuration.");
        if (string.IsNullOrWhiteSpace(p.MeasurementReference) || p.MeasurementReference.Length is < 10 or > 1000)
            errors.Add("Energy calibration needs a measurement reference of 10–1000 characters.");
        if (p.MeasuredAtUtc > now || p.ValidUntilUtc <= p.MeasuredAtUtc || p.ValidUntilUtc <= (through ?? now))
            errors.Add("Energy measurements must be in the past and remain valid through the mission end.");
        if (p.PayloadKg is < 0 or > 1000 || p.MaxWindMetersPerSecond < limits.MaxGustMetersPerSecond
            || p.MaxWindMetersPerSecond > 50 || p.MinTemperatureCelsius < -50 || p.MaxTemperatureCelsius > 100
            || p.MinTemperatureCelsius >= p.MaxTemperatureCelsius)
            errors.Add("The measured payload, wind and temperature envelope is invalid or narrower than the approved gust limit.");
        if (p.UsableCapacityWh is <= 0 or > 100000 || new[] { p.CruisePowerWatts, p.ClimbPowerWatts,
            p.DescentPowerWatts, p.LandingPowerWatts, p.HoverPowerWatts }.Any(w => w is <= 0 or > 1000000))
            errors.Add("Measured usable capacity and upper-bound phase power must be positive and within supported ranges.");
        if (p.MinimumGroundSpeedMetersPerSecond is < 0.5m or > 5 || p.ReturnAltitudeMeters is <= 0
            || p.ReturnAltitudeMeters > limits.MaxAltitudeMeters || p.LandingDurationSeconds is < 5 or > 600
            || p.HoldDurationSeconds is < 0 or > 600 || p.EnergyMarginFactor is < 1.2m or > 3)
            errors.Add("Recovery needs bounded ground speed, a return height within the ceiling, landing duration 5–600 s and energy margin 1.2–3.");
        var curve = p.RemainingEnergyCurve;
        if (curve == null || curve.Count is < 3 or > 101 || curve.Any(x => x == null) || curve[0].BatteryPercent != 0 || curve[0].LowerBoundWh != 0
            || curve[^1].BatteryPercent != 100 || curve.Any(x => x.BatteryPercent is < 0 or > 100 || x.LowerBoundWh < 0 || x.LowerBoundWh > p.UsableCapacityWh)
            || curve.Zip(curve.Skip(1)).Any(pair => pair.First.BatteryPercent >= pair.Second.BatteryPercent || pair.First.LowerBoundWh > pair.Second.LowerBoundWh))
            errors.Add("Remaining-energy curve needs 3–101 ordered measured points, starting at 0%/0 Wh and ending at 100%, with nondecreasing lower-bound Wh.");
        return errors;
    }

    public static RecoveryEnergyAssessment Assess(Mission mission, TelemetryRecord? telemetry, RecoveryPosition? home,
        DateTimeOffset now)
    {
        var limits = mission.AutomaticFlightLimits;
        var profile = limits?.RecoveryEnergyProfile;
        var missing = new List<string>();
        if (limits == null || profile == null) missing.Add("A measured recovery energy profile has not been supplied.");
        else
        {
            missing.AddRange(ProfileErrors(profile, limits, now, mission.EndTime is { } end ? new DateTimeOffset(DateTime.SpecifyKind(end, DateTimeKind.Utc)) : null, mission.UAVId));
            missing.AddRange(AutomaticFlightPolicy.Errors(mission.AutomaticFlightRequested, mission.AltitudeReference,
                mission.AverageSpeedMetersPerSecond ?? 0, mission.MaxSpeedMetersPerSecond ?? 0, limits,
                mission.Waypoints.Select(p => (p.Altitude, p.ActionType))));
        }
        var fresh = telemetry is { IsValid: true, Latitude: >= -90 and <= 90, Longitude: >= -180 and <= 180,
                Altitude: >= 0, BatteryLevel: >= 0 and <= 100, AltitudeReference: "Relative" }
            && telemetry.UAVId == mission.UAVId && telemetry.MissionId == mission.Id
            && telemetry.SourceTimestamp <= now && telemetry.SourceTimestamp >= now.AddSeconds(-3)
            && telemetry.ReceivedAt <= now && telemetry.ReceivedAt >= now.AddSeconds(-3);
        if (!fresh) missing.Add("Recovery estimates require fresh position, relative altitude and battery telemetry (3 seconds) from this mission/UAV.");
        if (home == null || !Coordinate(home.Latitude, home.Longitude) || home.RelativeAltitudeMeters != 0)
            missing.Add("A fresh actual FC Home reference is unavailable.");
        if (profile != null && fresh && (telemetry!.BatteryTemperatureCelsius == null
            || telemetry.BatteryTemperatureCelsius < profile.MinTemperatureCelsius || telemetry.BatteryTemperatureCelsius > profile.MaxTemperatureCelsius))
            missing.Add("Battery temperature is missing or outside the measured energy envelope.");
        if (profile != null && (mission.AverageSpeedMetersPerSecond < profile.MinimumGroundSpeedMetersPerSecond
            || mission.MaxSpeedMetersPerSecond < profile.MinimumGroundSpeedMetersPerSecond))
            missing.Add("Approved speed is below the measured minimum ground speed; do not extrapolate power outside measured conditions.");
        // Never interpolate above a measured lower bound or use max pack voltage.
        decimal? available = missing.Count == 0 ? profile!.RemainingEnergyCurve.Last(p => p.BatteryPercent <= telemetry!.BatteryLevel).LowerBoundWh : null;
        decimal? reserve = available.HasValue ? profile!.UsableCapacityWh * 0.2m : null;
        var candidates = mission.Waypoints.Where(w => w.Navigation?.EmergencyLandingCandidate == true
            || string.Equals(w.ActionType, "Land", StringComparison.OrdinalIgnoreCase)).OrderBy(w => w.SequenceOrder);
        var sites = new List<RecoveryEnergyEstimate>();
        foreach (var point in candidates)
        {
            var reasons = new List<string>();
            var nav = point.Navigation;
            var survey = nav?.LandingSiteSurvey;
            var inspected = survey is { GroundClear: true, ApproachClear: true }
                && survey.SurveyedAtUtc <= now && survey.ValidUntilUtc > now
                && !string.IsNullOrWhiteSpace(survey.EvidenceReference);
            if (!inspected) reasons.Add("Current pilot-reported ground and approach survey is missing; site is not independently verified.");
            if (nav?.LandingGroundAltitudeMeters is not >= 0) reasons.Add("Landing ground elevation is required.");
            if (mission.Status != "Approved") reasons.Add("The exact mission and site declaration have not been approved.");
            if (!Coordinate(point.Latitude, point.Longitude)) reasons.Add("Landing coordinates are invalid.");
            decimal? distance = fresh && Coordinate(point.Latitude, point.Longitude)
                ? Distance(telemetry!.Latitude!.Value, telemetry.Longitude!.Value, point.Latitude, point.Longitude) : null;
            decimal? required = null;
            if (available.HasValue && distance.HasValue && nav?.LandingGroundAltitudeMeters is { } ground && ground >= 0)
            {
                var cruiseAltitude = Math.Max(telemetry!.Altitude!.Value, profile!.ReturnAltitudeMeters);
                if (cruiseAltitude > limits!.MaxAltitudeMeters || ground >= cruiseAltitude)
                    reasons.Add("Return/landing geometry falls outside the approved altitude envelope.");
                else
                {
                    var speed = Math.Min(profile.MinimumGroundSpeedMetersPerSecond, mission.AverageSpeedMetersPerSecond ?? 0);
                    if (speed <= 0) reasons.Add("Approved recovery ground speed is unavailable.");
                    else
                    {
                        var climb = (cruiseAltitude - telemetry.Altitude.Value) / limits.ClimbSpeedMetersPerSecond;
                        var descent = (cruiseAltitude - ground) / Math.Min(limits.DescentSpeedMetersPerSecond, limits.LandingSpeedMetersPerSecond);
                        var wh = (distance.Value / speed * profile.CruisePowerWatts + climb * profile.ClimbPowerWatts
                            + descent * profile.DescentPowerWatts + profile.LandingDurationSeconds * profile.LandingPowerWatts
                            + profile.HoldDurationSeconds * profile.HoverPowerWatts) / 3600 * profile.EnergyMarginFactor;
                        required = decimal.Round(wh, 3, MidpointRounding.ToPositiveInfinity);
                    }
                }
            }
            sites.Add(new($"waypoint:{point.SequenceOrder}", distance.HasValue ? decimal.Round(distance.Value, 2) : null, required,
                required.HasValue ? required.Value + reserve <= available : null, inspected, reasons.ToArray()));
        }
        if (sites.Count == 0) missing.Add("No landing endpoint or emergency landing candidate is available.");
        if (fresh && telemetry!.BatteryLevel <= 15)
            missing.Add("Critical battery takes priority: do not initiate a new return leg; the verified native emergency procedure is required.");
        missing.Add("Measured-condition/payload matching, route obstruction checks and FC enforcement remain required; these are planning estimates, not recovery authorization.");
        return new(available.HasValue ? "PlanningEstimate" : "MissingData", available, reserve, sites, missing);
    }

    private static bool Coordinate(decimal latitude, decimal longitude) => latitude is >= -90 and <= 90 && longitude is >= -180 and <= 180;
    private static decimal Distance(decimal latA, decimal lonA, decimal latB, decimal lonB)
    {
        static double R(decimal d) => (double)d * Math.PI / 180;
        var a = Math.Pow(Math.Sin(R(latB - latA) / 2), 2) + Math.Cos(R(latA)) * Math.Cos(R(latB)) * Math.Pow(Math.Sin(R(lonB - lonA) / 2), 2);
        return (decimal)(6371000 * 2 * Math.Asin(Math.Sqrt(Math.Clamp(a, 0, 1))));
    }
}
