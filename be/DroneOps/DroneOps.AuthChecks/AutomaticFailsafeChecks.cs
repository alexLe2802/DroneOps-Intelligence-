using DroneOps.Application.Services.Missions;
using DroneOps.Domain.Entities;

internal static class AutomaticFailsafeChecks
{
    public static void Run()
    {
        var now = DateTimeOffset.UtcNow; var limits = new AutomaticFlightLimits(); var passed = 0;
        void Check(bool value, string name) { if (!value) throw new Exception("FAIL: " + name); Console.WriteLine("PASS: " + name); passed++; }
        var sites = new[] { new RecoverySite("home", true, true, true, true, 200, 20),
            new RecoverySite("unsafe-nearest", false, true, false, true, 1, 1),
            new RecoverySite("alternate", false, true, true, true, 50, 5),
            new RecoverySite("farther", false, true, true, true, 100, 3) };
        var input = new FailsafeEvidence(now, false, true, false, false, 70, true, 40, 5, 3, 0, 20, 0, sites);
        AutomaticFailsafeDecision Decide(FailsafeEvidence x) => AutomaticFailsafePolicy.Decide(x, 3, limits, now);
        Check(Decide(input).Action == "ContinueMonitoring", "healthy flight remains monitored");
        Check(Decide(input with { ControlLost = true }).TargetSiteId == "home", "control loss returns to verified Home when energy including reserve is sufficient");
        Check(Decide(input with { BatteryPercent = 25 }).TargetSiteId == "home", "low battery prefers reachable Home");
        var alternate = Decide(input with { BatteryPercent = 25, ControlLost = true, AvailableEnergyWh = 11 });
        Check(alternate.Action == "LandAtApprovedAlternate" && alternate.TargetSiteId == "alternate", "combined low battery/control loss excludes unsafe nearest waypoint and selects nearest reachable verified landing site");
        Check(alternate.Triggers.Contains("LowBattery") && alternate.Triggers.Contains("ControlLoss") && alternate.Triggers.Contains("InsufficientEnergyForHome"), "all simultaneous faults remain visible");
        Check(Decide(input with { AvailableEnergyWh = 11 }).TargetSiteId == "alternate", "insufficient Home return energy triggers recovery before low percentage threshold");
        Check(Decide(input with { BatteryPercent = 25, AvailableEnergyWh = 1 }).Action == "NativeEmergencyLanding", "unreachable alternatives cannot authorize another flight leg");
        Check(Decide(input with { ControlLost = true, CalibratedEnergyModel = false }).Action == "NativeConfiguredFailsafe", "uncalibrated battery heuristic never authorizes a return leg");
        Check(Decide(input with { ControlLost = true, LandingReserveWh = 0 }).Action == "NativeConfiguredFailsafe", "missing landing reserve cannot count as verified energy sufficiency");
        Check(Decide(input with { NavigationHealthy = false, ControlLost = true }).Action == "NativeConfiguredFailsafe", "position loss blocks navigation-based RTL");
        Check(Decide(input with { BatteryPercent = null }).Action == "NativeConfiguredFailsafe", "unknown battery is not treated as full battery");
        Check(Decide(input with { BatteryPercent = 0 }).Action == "NativeEmergencyLanding", "zero reported percent triggers emergency procedure without claiming power remains");
        Check(Decide(input with { ObservedAt = now.AddSeconds(-4), ControlLost = true }).Action == "NativeConfiguredFailsafe", "stale evidence cannot authorize routing");
        Check(Decide(input with { HorizontalSpeedMetersPerSecond = 5, EnvelopeBreachSeconds = 0.1m }).Action == "ContinueMonitoring", "a single noisy sample does not issue a recovery maneuver");
        Check(Decide(input with { HorizontalSpeedMetersPerSecond = 5, EnvelopeBreachSeconds = 2 }).Action == "HoldAndReassess", "sustained horizontal overspeed stops mission progression");
        Check(Decide(input with { VerticalSpeedMetersPerSecond = 2, EnvelopeBreachSeconds = 2 }).Triggers.Contains("AscentOverspeed"), "climb overspeed detected");
        Check(Decide(input with { VerticalSpeedMetersPerSecond = -2, EnvelopeBreachSeconds = 2 }).Triggers.Contains("DescentOverspeed"), "descent overspeed detected");
        Check(Decide(input with { AltitudeMeters = 34, EnvelopeBreachSeconds = 2 }).Triggers.Contains("AltitudeExceeded"), "sustained altitude breach detected");
        Check(Decide(input with { RecoveryAlreadyActive = true }).Action == "ContinueVerifiedRecovery", "restored radio cannot resume a mission automatically");
        Check(Decide(input with { RecoveryAlreadyActive = true, BatteryPercent = 25, AvailableEnergyWh = 11 }).TargetSiteId == "alternate", "an active RTL is reevaluated when battery no longer permits Home");
        Check(Decide(input with { RecoveryAlreadyActive = true, FlightPhase = "Landing", ControlLost = true, BatteryPercent = 25 }).Action == "ContinueVerifiedRecovery", "control-loss RTL does not interrupt a committed landing");
        for (var flags = 0; flags < 32; flags++)
        {
            var fault = input with { FcBatteryCritical = true, ControlLost = (flags & 1) != 0,
                NavigationHealthy = (flags & 2) == 0, HorizontalSpeedMetersPerSecond = (flags & 4) != 0 ? 20 : 3,
                VerticalSpeedMetersPerSecond = (flags & 8) != 0 ? -5 : 0,
                RecoveryAlreadyActive = (flags & 16) != 0, EnvelopeBreachSeconds = 3 };
            var decision = Decide(fault);
            Check(decision.Action == "NativeEmergencyLanding" && decision.TargetSiteId == null
                && !decision.AutoResumeAllowed && !decision.FcExecutionVerified, $"critical battery has priority in combined-fault case {flags}");
        }
        Check(Decide(input with { OnGround = true, FcBatteryCritical = true, ControlLost = true }).Action == "InhibitLaunch", "ground fault inhibits launch without issuing ARM or force-disarm");
        Console.WriteLine($"Automatic failsafe policy checks passed: {passed}");
    }
}
