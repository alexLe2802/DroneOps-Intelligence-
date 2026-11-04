using DroneOps.Domain.Entities;

namespace DroneOps.Application.Services.Missions;

public static class AutomaticFlightPolicy
{
    public static IReadOnlyList<string> NavigationErrors(IEnumerable<(WaypointNavigation? Navigation, string? Action)> points, decimal maximum)
    {
        var errors = new List<string>();
        foreach (var (nav, action) in points)
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
        }
        return errors.Distinct().ToArray();
    }
    public const decimal DefaultCruise = 3m, MaximumHorizontal = 5m, MaximumAltitude = 30m;
    public static IReadOnlyList<string> Errors(bool automatic, string? frame, decimal cruise, decimal maximum,
        AutomaticFlightLimits? limits, IEnumerable<(decimal? Altitude, string? Action)> points)
    {
        if (!automatic) return [];
        var errors = new List<string>();
        if (limits == null) { errors.Add("Automatic flight requires an explicit operational envelope. Resubmit the mission for approval."); return errors; }
        if (limits.PolicyVersion != "multicopter-envelope-v1") errors.Add("Unsupported automatic-flight policy version.");
        if (frame != "Relative") errors.Add("Automatic-flight v1 requires Relative altitude. Terrain/MSL profiles need separate verified support.");
        if (cruise < 0.5m || maximum > MaximumHorizontal || maximum < cruise || maximum <= 0)
            errors.Add("Automatic cruise must be >= 0.5 m/s and <= the mission maximum; horizontal maximum must be <= 5 m/s (18 km/h).");
        if (limits.TakeoffSpeedMetersPerSecond is < 1m or > 1.5m) errors.Add("Automatic takeoff speed must be 1–1.5 m/s.");
        if (limits.ClimbSpeedMetersPerSecond < limits.TakeoffSpeedMetersPerSecond || limits.ClimbSpeedMetersPerSecond > 1.5m)
            errors.Add("Climb speed must be >= takeoff speed and <= 1.5 m/s.");
        if (limits.DescentSpeedMetersPerSecond is < 0.6m or > 1m) errors.Add("Automatic descent speed must be 0.6–1 m/s.");
        if (limits.LandingSpeedMetersPerSecond is < 0.6m or > 0.7m || limits.LandingSpeedMetersPerSecond > limits.DescentSpeedMetersPerSecond)
            errors.Add("Landing speed must be 0.6–0.7 m/s and <= descent speed.");
        if (limits.MaxAltitudeMeters is <= 10m or > MaximumAltitude) errors.Add("Automatic altitude ceiling must be > 10 m and <= 30 m above takeoff home.");
        if (limits.MaxWindMetersPerSecond is <= 0m or > 3m || limits.MaxGustMetersPerSecond < limits.MaxWindMetersPerSecond || limits.MaxGustMetersPerSecond > 5m)
            errors.Add("Automatic wind limit must be > 0 and <= 3 m/s; gust limit must be >= wind and <= 5 m/s.");
        if (points.Any(p => p.Altitude is null || p.Altitude < 0 || p.Altitude > limits.MaxAltitudeMeters))
            errors.Add("Every automatic mission waypoint must have an altitude within its approved altitude ceiling.");
        return errors;
    }
}
