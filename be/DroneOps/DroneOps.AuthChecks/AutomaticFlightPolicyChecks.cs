using DroneOps.Application.Services.Missions;
using DroneOps.Application.Validators.Missions;
using DroneOps.Domain.Entities;

internal static class AutomaticFlightPolicyChecks
{
    public static void Run()
    {
        var passed = 0;
        void Check(bool value, string name) { if (!value) throw new Exception("FAIL: " + name); Console.WriteLine("PASS: " + name); passed++; }
        var limits = new AutomaticFlightLimits();
        var points = new[] { ((decimal?)20m, (string?)"TakeOff"), ((decimal?)20m, (string?)"Land") };
        bool Valid(AutomaticFlightLimits? value, decimal cruise = 3m, decimal maximum = 5m, string frame = "Relative") =>
            AutomaticFlightPolicy.Errors(true, frame, cruise, maximum, value, points).Count == 0;
        Check(Valid(limits), "trial multicopter defaults pass");
        var v2 = AutomaticFlightPolicy.DefaultLimits();
        Check(Valid(v2, 11.2m, 15m) && v2.TakeoffSpeedMetersPerSecond == 6.5m && v2.LandingSpeedMetersPerSecond == 1m,
            "v2 higher default speeds pass their versioned limits");
        Check(!Valid(v2, 11.2m, 15.1m) && !Valid(v2 with { ClimbSpeedMetersPerSecond = 8.1m }, 11.2m, 15m)
            && !Valid(v2 with { DescentSpeedMetersPerSecond = 6.1m }, 11.2m, 15m)
            && !Valid(v2 with { LandingSpeedMetersPerSecond = 1.6m }, 11.2m, 15m), "v2 speed ceilings are enforced by API");
        Check(!Valid(null), "legacy automatic mission needs explicit approved limits");
        Check(!Valid(limits, 6, 6), "horizontal cap cannot be bypassed through API");
        Check(!Valid(limits, 4, 3), "cruise cannot exceed selected maximum");
        Check(!Valid(limits, 0.1m), "near-zero cruise cannot bypass policy");
        Check(Valid(limits, 2.77m, 2.77m), "below-10 km/h target can be represented without rounding upward");
        Check(!Valid(limits with { MaxAltitudeMeters = 31 }), "altitude ceiling above 30 m rejected");
        Check(!Valid(limits with { MaxAltitudeMeters = 19 }), "waypoint above selected ceiling rejected");
        Check(!Valid(limits, frame: "MSL") && !Valid(limits, frame: "AGL"), "absolute/terrain heights are not misinterpreted as relative ceiling");
        Check(!Valid(limits with { TakeoffSpeedMetersPerSecond = 2 }), "takeoff rate above cap rejected");
        Check(!Valid(limits with { TakeoffSpeedMetersPerSecond = 1.5m, ClimbSpeedMetersPerSecond = 1 }), "takeoff cannot exceed climb cap");
        Check(!Valid(limits with { ClimbSpeedMetersPerSecond = 2 }), "climb above cap rejected");
        Check(!Valid(limits with { DescentSpeedMetersPerSecond = 1.1m }), "descent above cap rejected");
        Check(!Valid(limits with { LandingSpeedMetersPerSecond = 0.8m }), "landing above cap rejected");
        Check(!Valid(limits with { LandingSpeedMetersPerSecond = 0.7m, DescentSpeedMetersPerSecond = 0.6m }), "landing cannot exceed descent cap");
        Check(!Valid(limits with { MaxWindMetersPerSecond = 4 }) && !Valid(limits with { MaxGustMetersPerSecond = 6 }), "wind and gust caps enforced");
        Check(!Valid(limits with { MaxGustMetersPerSecond = 2 }), "gust threshold cannot be below sustained wind threshold");
        Check(!Valid(limits with { PolicyVersion = "unapproved" }), "unknown policy cannot authorize automatic flight");
        Check(AutomaticFlightPolicy.Errors(false, "MSL", 20, 25, null, points).Count == 0, "manual mission policy remains separate");
        var mission = new Mission { AutomaticFlightRequested = true, AutomaticFlightLimits = limits };
        var hash = MissionValidationService.Hash(mission);
        mission.AutomaticFlightLimits = limits with { LandingSpeedMetersPerSecond = 0.6m };
        Check(hash != MissionValidationService.Hash(mission), "vertical speed change invalidates consent and approval hash");
        var dto = MissionPlans.Dto(mission);
        Check(dto.AutomaticFlightLimits == mission.AutomaticFlightLimits, "edit/review DTO preserves vertical rates and policy");
        var proposed = MissionPlans.Propose(mission, dto);
        Check(proposed.AutomaticFlightLimits == dto.AutomaticFlightLimits, "proposed plan retains exact operating envelope");
        var home = new PlannedHome(10.7m, 106.7m);
        var nav = new WaypointNavigation(PlannedHome: home, SpeedMetersPerSecond: 1, HoldTimeSeconds: 5, AcceptanceRadiusMeters: 1.5m, YawDegrees: 90);
        Check(AutomaticFlightPolicy.NavigationErrors([(nav, "Hover")], 3).Count == 0, "supported optional waypoint overrides pass");
        Check(AutomaticFlightPolicy.NavigationErrors([(nav with { SpeedMetersPerSecond = 4 }, "Hover")], 3).Count > 0, "waypoint override cannot exceed approved mission speed");
        Check(AutomaticFlightPolicy.NavigationErrors([(null, "Hover")], 3).Count > 0, "Hover requires its own hold duration");
        Check(AutomaticFlightPolicy.NavigationErrors([(nav with { PassRadiusMeters = 2 }, "Hover")], 3).Count > 0, "unsupported pass radius is not silently ignored");
        Check(AutomaticFlightPolicy.NavigationErrors([(nav with { EmergencyLandingCandidate = true }, "Hover")], 3).Count > 0, "normal waypoint is not silently promoted to a safe landing site");
        mission.UAVId = Guid.NewGuid(); mission.AltitudeReference = "Relative"; mission.AverageSpeedMetersPerSecond = 3; mission.MaxSpeedMetersPerSecond = 3;
        mission.Waypoints = new[] {
            new Waypoint { SequenceOrder=1, Latitude=10.7m, Longitude=106.7m, Altitude=20, ActionType="TakeOff", Navigation=new(PlannedHome:home) },
            new Waypoint { SequenceOrder=2, Latitude=10.701m, Longitude=106.701m, Altitude=20, ActionType="Hover", Navigation=nav with { PlannedHome = null } },
            new Waypoint { SequenceOrder=3, Latitude=10.7m, Longitude=106.7m, Altitude=20, ActionType="Land", Navigation=new(LandingGroundAltitudeMeters:0) }
        };
        var parameters = new FirmwareParameterSnapshot("1.17.0", new() { ["MPC_XY_CRUISE"]=2, ["NAV_ACC_RAD"]=2 }, DateTimeOffset.UtcNow);
        var compilation = MissionCommandCompiler.Compile(mission, new("PX4", YawPolicy:"Autopilot"), parameters);
        Check(compilation.CanEncode, "observed FC parameter snapshot can resolve optional radius/cruise for a preview");
        var hover = compilation.Items.Single(i => i.Command == 16 && i.X == 107010000 && i.Y == 1067010000);
        Check(hover.Param1 == 5 && hover.Param2 == 1.5f && hover.Param4 == 90, "hold, radius and yaw overrides compile into mission item parameters");
        Check(compilation.Items.Count(i => i.Command == 16) == 2
            && compilation.Items[^2] is { Command: 16, X: 107000000, Y: 1067000000, Z: 20, Param1: 0, Param2: 2 },
            "final arrival waypoint uses its approved altitude and FC radius without inheriting Hover overrides");
        Check(compilation.Items.Where(i => i.Command == 178).Select(i => i.Param2).SequenceEqual(new float[] { 2, 1, 2 }), "FC cruise is bounded and per-waypoint override is restored at the next node");
        Check(compilation.Items.Last().Z == 0 && compilation.OperationalEnvelope?.FcParameterVerificationRequired == true && !compilation.CanStart, "LAND uses supplied ground; preview cannot bypass FC profile verification");
        var unchanged = MissionCommandCompiler.Compile(mission, new("PX4", YawPolicy:"Autopilot"), parameters with { ObservedAtUtc = parameters.ObservedAtUtc.AddSeconds(1) });
        Check(compilation.CompiledMissionHash == unchanged.CompiledMissionHash, "refreshing the same FC values does not alter the compiled mission hash");
        mission.AutomaticFlightLimits = v2;
        mission.AverageSpeedMetersPerSecond = 11.2m; mission.MaxSpeedMetersPerSecond = 15;
        var beforeApply = new FirmwareParameterSnapshot("4.7.1", new() { ["WP_RADIUS_M"] = 2, ["WP_SPD"] = 10 }, DateTimeOffset.UtcNow);
        var v2Plan = MissionCommandCompiler.Compile(mission, new("ArduPilot"), beforeApply);
        var afterApply = MissionCommandCompiler.Compile(mission, new("ArduPilot"), beforeApply with { Values = new() { ["WP_RADIUS_M"] = 2, ["WP_SPD"] = 15 } });
        Check(v2Plan.CanEncode && v2Plan.Items.First(i => i.Command == 178).Param2 == 11.2f
            && v2Plan.CompiledMissionHash == afterApply.CompiledMissionHash,
            "v2 keeps approved speed and stable compiled hash when the adapter raises an older FC speed default");
        var batteryUav = new UAV { BatteryCapacityMah=1000, BatteryMaxVoltage=25.2m, MaxFlightTimeMinutes=20 };
        var maxOnly = MissionBatteryRiskCalculator.Calculate(mission, batteryUav, null);
        Check(maxOnly.BatteryEnergyWh == null, "full-charge maximum voltage is never substituted for nominal battery energy");
        batteryUav.BatteryVoltage = 22.2m;
        Check(MissionBatteryRiskCalculator.Calculate(mission, batteryUav, null).BatteryEnergyWh == 22.2m, "energy uses nominal voltage when supplied, preserving legacy semantics");
        Console.WriteLine($"Automatic flight policy checks passed: {passed}");
    }
}
