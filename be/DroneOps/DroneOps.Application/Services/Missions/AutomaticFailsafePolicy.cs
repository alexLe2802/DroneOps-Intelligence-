namespace DroneOps.Application.Services.Missions;

public sealed record RecoverySite(string Id, bool IsHome, bool MissionApproved, bool LandingVerified,
    bool ApproachClear, decimal DistanceMeters, decimal? EnergyToLandWh);
public sealed record FailsafeEvidence(DateTimeOffset ObservedAt, bool OnGround, bool NavigationHealthy,
    bool ControlLost, bool FcBatteryCritical, decimal? BatteryPercent, bool CalibratedEnergyModel,
    decimal? AvailableEnergyWh, decimal LandingReserveWh, decimal? HorizontalSpeedMetersPerSecond,
    decimal? VerticalSpeedMetersPerSecond, decimal? AltitudeMeters, decimal EnvelopeBreachSeconds,
    IReadOnlyList<RecoverySite> Sites, bool RecoveryAlreadyActive = false, string FlightPhase = "Mission");
// VerticalSpeedMetersPerSecond is positive UP; a MAVLink adapter must negate
// GLOBAL_POSITION_INT.vz (positive DOWN, cm/s) and convert units before calling.
public sealed record AutomaticFailsafeDecision(string Action, string? TargetSiteId, string Reason,
    IReadOnlyList<string> Triggers, bool AutoResumeAllowed = false, bool FcExecutionVerified = false);
public sealed record AutomaticRecoveryContract(decimal LowBatteryPercent, decimal CriticalBatteryPercent,
    decimal LandingReservePercent, decimal HorizontalSpeedToleranceMetersPerSecond, decimal VerticalSpeedToleranceMetersPerSecond,
    decimal AltitudeToleranceMeters, int BreachDwellSeconds, bool FcVerificationRequired = true, bool ExecutionEnabled = false);

// Decision contract only. This cannot be used as proof that an FC parameter or
// aircraft action has been applied. Native firmware configuration and a tested
// gateway adapter are required before enabling flight execution.
public static class AutomaticFailsafePolicy
{
    public static AutomaticRecoveryContract Contract(decimal maximum) => new(30, 15, 20, Math.Max(0.5m, maximum * 0.2m), 0.3m, 2m, 2);
    public static AutomaticFailsafeDecision Decide(FailsafeEvidence input, decimal maximumHorizontal,
        DroneOps.Domain.Entities.AutomaticFlightLimits limits, DateTimeOffset now)
    {
        var triggers = new List<string>();
        var stale = input.ObservedAt > now || now - input.ObservedAt > TimeSpan.FromSeconds(3);
        var critical = input.FcBatteryCritical || input.BatteryPercent is >= 0 and <= 15;
        var low = input.BatteryPercent is >= 0 and <= 30;
        if (critical) triggers.Add("CriticalBattery"); else if (low) triggers.Add("LowBattery");
        if (input.ControlLost) triggers.Add("ControlLoss");
        if (!input.NavigationHealthy) triggers.Add("NavigationUnavailable");
        if (stale) triggers.Add("StaleTelemetry");
        if (input.BatteryPercent is not (>= 0 and <= 100)) triggers.Add("BatteryDataUnavailable");
        var speedBreach = input.HorizontalSpeedMetersPerSecond > maximumHorizontal + Math.Max(0.5m, maximumHorizontal * 0.2m);
        var climbBreach = input.VerticalSpeedMetersPerSecond > (input.FlightPhase == "Takeoff" ? limits.TakeoffSpeedMetersPerSecond : limits.ClimbSpeedMetersPerSecond) + 0.3m;
        var descentBreach = input.VerticalSpeedMetersPerSecond < -((input.FlightPhase == "Landing" ? limits.LandingSpeedMetersPerSecond : limits.DescentSpeedMetersPerSecond) + 0.3m);
        var altitudeBreach = input.AltitudeMeters > limits.MaxAltitudeMeters + 2m;
        if (input.EnvelopeBreachSeconds >= 2)
        {
            if (speedBreach) triggers.Add("HorizontalOverspeed");
            if (climbBreach) triggers.Add("AscentOverspeed");
            if (descentBreach) triggers.Add("DescentOverspeed");
            if (altitudeBreach) triggers.Add("AltitudeExceeded");
        }
        AutomaticFailsafeDecision Result(string action, string reason, string? site = null) => new(action, site, reason, triggers);
        if (input.OnGround) return Result("InhibitLaunch", "Do not arm/start automatically; motor state remains subject to pilot and native FC safety.");
        if (critical) return Result("NativeEmergencyLanding", "Critical battery takes priority over RTL, control-loss recovery and overspeed. Do not begin a new horizontal return leg.");
        if (stale || !input.NavigationHealthy)
            return Result("NativeConfiguredFailsafe", "No navigation command is authorized from stale or invalid position evidence. Use the previously verified FC fallback.");
        if (input.FlightPhase == "Landing" && input.RecoveryAlreadyActive)
            return Result("ContinueVerifiedRecovery", "Do not interrupt a committed recovery landing with a new RTL request.");
        if (input.BatteryPercent is not (>= 0 and <= 100))
            return Result("NativeConfiguredFailsafe", "Battery state is unknown. Do not authorize a new return leg from a missing percentage.");
        var energyValid = input.CalibratedEnergyModel && input.AvailableEnergyWh is >= 0 && input.LandingReserveWh > 0;
        bool Reachable(RecoverySite site) => energyValid && site.MissionApproved && site.LandingVerified && site.ApproachClear
            && site.DistanceMeters >= 0 && site.EnergyToLandWh is >= 0
            && site.EnergyToLandWh.Value + input.LandingReserveWh <= input.AvailableEnergyWh!.Value;
        var home = input.Sites.FirstOrDefault(s => s.IsHome && Reachable(s));
        var knownHomeUnreachable = energyValid && input.Sites.Any(s => s.IsHome && s.MissionApproved && s.LandingVerified && s.ApproachClear
            && s.EnergyToLandWh is >= 0 && s.EnergyToLandWh.Value + input.LandingReserveWh > input.AvailableEnergyWh!.Value);
        if (knownHomeUnreachable) triggers.Add("InsufficientEnergyForHome");
        if (input.RecoveryAlreadyActive && !low && !input.ControlLost && !knownHomeUnreachable)
            return Result("ContinueVerifiedRecovery", "A recovered link or cleared overspeed cannot restart the mission. Recheck energy on every subsequent sample.");
        if (!low && !input.ControlLost && !knownHomeUnreachable && !triggers.Any(t => t.EndsWith("Overspeed", StringComparison.Ordinal) || t == "AltitudeExceeded"))
            return Result("ContinueMonitoring", "No recovery trigger is established.");
        if (!low && !input.ControlLost && !knownHomeUnreachable)
            return Result("HoldAndReassess", "Sustained envelope breach: stop mission progression and reduce motion using the verified firmware adapter; do not disarm in air.");
        if (!energyValid) return Result("NativeConfiguredFailsafe", "An uncalibrated endurance/percent heuristic cannot authorize flight to Home or a waypoint.");
        if (home != null) return Result("ReturnHome", "Home approach and energy-to-land plus reserve are verified reachable.", home.Id);
        var alternate = input.Sites.Where(s => !s.IsHome && Reachable(s)).OrderBy(s => s.DistanceMeters).ThenBy(s => s.EnergyToLandWh).FirstOrDefault();
        return alternate != null
            ? Result("LandAtApprovedAlternate", "Home is not verified reachable. Select the nearest approved, verified and energy-reachable landing site, not a normal waypoint.", alternate.Id)
            : Result("NativeEmergencyLanding", "No approved landing site is verified energy-reachable. Use the verified onboard emergency procedure.");
    }
}
