using DroneOps.Domain.Entities;

namespace DroneOps.Application.Services.Missions;

public static class AutomaticFlightPolicy
{
    public static IReadOnlyList<string> NavigationErrors(IEnumerable<(WaypointNavigation? Navigation, string? Action)> points, decimal maximum)
    {
        var errors = new List<string>();
        var route = points.ToList();
        if (route.FirstOrDefault().Navigation?.PlannedHome == null)
            errors.Add("Planned Home is required for automatic missions. Select the expected launch location.");
        foreach (var (nav, action) in route)
        {
            var waypointCommand = string.Equals(action, "FlyThrough", StringComparison.OrdinalIgnoreCase) || string.Equals(action, "Hover", StringComparison.OrdinalIgnoreCase);
            if (!waypointCommand && (nav?.HoldTimeSeconds > 0 || nav?.AcceptanceRadiusMeters != null)) errors.Add("Hold time and acceptance radius overrides apply only to FlyThrough/Hover commands.");
            if (string.Equals(action, "TakeOff", StringComparison.OrdinalIgnoreCase) && nav?.SpeedMetersPerSecond != null) errors.Add("TakeOff uses the approved vertical takeoff rate; horizontal waypoint-speed override does not apply.");
            if (nav?.SpeedMetersPerSecond is { } speed && (speed < 0.5m || speed > maximum)) errors.Add("Waypoint speed must be >= 0.5 m/s and <= the approved mission maximum.");
            if (nav?.HoldTimeSeconds is < 0 or > 600) errors.Add("Waypoint hold time must be 0–600 seconds.");
            if (string.Equals(action, "Hover", StringComparison.OrdinalIgnoreCase) && nav?.HoldTimeSeconds is not > 0) errors.Add("Hover requires a positive hold time at that waypoint.");
            if (nav?.AcceptanceRadiusMeters is { } radius && (radius <= 0 || radius > 10)) errors.Add("Acceptance radius must be > 0 and <= 10 m.");
            if (nav?.PassRadiusMeters is { } pass && pass != 0) errors.Add("Nonzero pass radius is not supported by the multicopter v1 adapter; leave it unset or 0.");
            if (nav?.YawDegrees is { } yaw && (yaw < 0 || yaw >= 360)) errors.Add("Waypoint yaw must be in [0,360) degrees.");
            if (nav?.EmergencyLandingCandidate == true && (nav.LandingGroundAltitudeMeters is null or < 0 || string.IsNullOrWhiteSpace(nav.LandingSiteNotes) || nav.LandingSiteNotes.Length < 10))
                errors.Add("A proposed emergency landing site needs its ground altitude and clearance notes (at least 10 characters); marking a waypoint does not verify landing safety.");
            if (nav?.LandingSiteNotes?.Length > 500) errors.Add("Landing-site notes must not exceed 500 characters.");
            if (nav?.LandingSiteSurvey is { } survey)
            {
                if (nav.EmergencyLandingCandidate != true && !string.Equals(action, "Land", StringComparison.OrdinalIgnoreCase))
                    errors.Add("A site survey applies only to the LAND endpoint or a proposed emergency landing candidate.");
                if (survey.ValidUntilUtc <= survey.SurveyedAtUtc || survey.SurveyedAtUtc > DateTimeOffset.UtcNow
                    || string.IsNullOrWhiteSpace(survey.EvidenceReference) || survey.EvidenceReference.Length is < 10 or > 1000)
                    errors.Add("A landing-site survey needs a past survey time, a later expiry and an evidence reference of 10–1000 characters.");
            }
        }
        return errors.Distinct().ToArray();
    }
    public const decimal DefaultCruise = 11.2m, MaximumHorizontal = 15m, MaximumAltitude = 30m;
    public static AutomaticFlightLimits DefaultLimits() => new(6.5m, 6.5m, 5.8m, 1m, PolicyVersion: "multicopter-envelope-v2");
    public static IReadOnlyList<string> Errors(bool automatic, string? frame, decimal cruise, decimal maximum,
        AutomaticFlightLimits? limits, IEnumerable<(decimal? Altitude, string? Action)> points)
    {
        if (!automatic) return [];
        var errors = new List<string>();
        if (limits == null) { errors.Add("Automatic flight requires an explicit operational envelope. Resubmit the mission for approval."); return errors; }
        if (limits.PolicyVersion is not ("multicopter-envelope-v1" or "multicopter-envelope-v2")) errors.Add("Unsupported automatic-flight policy version.");
        if (frame != "Relative") errors.Add("Automatic flight requires Relative altitude. Terrain/MSL profiles need separate verified support.");
        var v2 = limits.PolicyVersion == "multicopter-envelope-v2";
        var horizontalCap = v2 ? 15m : 5m;
        var climbCap = v2 ? 8m : 1.5m;
        var descentCap = v2 ? 6m : 1m;
        var landingCap = v2 ? 1.5m : 0.7m;
        if (cruise < 0.5m || maximum > horizontalCap || maximum < cruise || maximum <= 0)
            errors.Add($"Automatic cruise must be >= 0.5 m/s and <= the mission maximum; horizontal maximum must be <= {horizontalCap} m/s.");
        if (limits.TakeoffSpeedMetersPerSecond < 1m || limits.TakeoffSpeedMetersPerSecond > climbCap) errors.Add($"Automatic takeoff speed must be 1–{climbCap} m/s.");
        if (limits.ClimbSpeedMetersPerSecond < limits.TakeoffSpeedMetersPerSecond || limits.ClimbSpeedMetersPerSecond > climbCap)
            errors.Add($"Climb speed must be >= takeoff speed and <= {climbCap} m/s.");
        if (limits.DescentSpeedMetersPerSecond < 0.6m || limits.DescentSpeedMetersPerSecond > descentCap) errors.Add($"Automatic descent speed must be 0.6–{descentCap} m/s.");
        if (limits.LandingSpeedMetersPerSecond < 0.6m || limits.LandingSpeedMetersPerSecond > landingCap || limits.LandingSpeedMetersPerSecond > limits.DescentSpeedMetersPerSecond)
            errors.Add($"Landing speed must be 0.6–{landingCap} m/s and <= descent speed.");
        if (limits.MaxAltitudeMeters is <= 10m or > MaximumAltitude) errors.Add("Automatic altitude ceiling must be > 10 m and <= 30 m above takeoff home.");
        if (limits.MaxWindMetersPerSecond is <= 0m or > 3m || limits.MaxGustMetersPerSecond < limits.MaxWindMetersPerSecond || limits.MaxGustMetersPerSecond > 5m)
            errors.Add("Automatic wind limit must be > 0 and <= 3 m/s; gust limit must be >= wind and <= 5 m/s.");
        if (points.Any(p => p.Altitude is null || p.Altitude < 0 || p.Altitude > limits.MaxAltitudeMeters))
            errors.Add("Every automatic mission waypoint must have an altitude within its approved altitude ceiling.");
        errors.AddRange(RecoveryEnergyCalculator.ProfileErrors(limits.RecoveryEnergyProfile, limits, DateTimeOffset.UtcNow));
        return errors;
    }
}
