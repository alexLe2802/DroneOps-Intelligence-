using DroneOps.Application.Services.Missions;
using DroneOps.Domain.Entities;

internal static class MissionCommandCompilerChecks
{
    public static void Run()
    {
        var mission = new Mission { Id = Guid.NewGuid(), UAVId = Guid.NewGuid(), Name = "Compiler fixture", AltitudeReference = "Relative",
            AverageSpeedMetersPerSecond = 5, MaxSpeedMetersPerSecond = 8,
            Waypoints = [Point(1, "TakeOff"), Point(2, "FlyThrough"), Point(3, "Hover"), Point(4, "Land")] };
        var options = new MissionCompileOptions("ArduPilot", AcceptanceRadiusMeters: 2, YawPolicy: "Autopilot", HoverDurationSeconds: 5, LandingGroundAltitudeMeters: 0);
        var withoutHome = MissionCommandCompiler.Compile(mission, options);
        var home = new PlannedHome(10.8231m, 106.6297m, 12m);
        mission.Waypoints.First().Navigation = new(PlannedHome: home);
        var withHome = MissionCommandCompiler.Compile(mission, options);
        Check(withHome.CanEncode && withHome.PlannedHome == home, "compiled FC payload contains the chosen Planned Home");
        Check(withHome.SourcePlanHash != withoutHome.SourcePlanHash && withHome.CompiledMissionHash != withoutHome.CompiledMissionHash,
            "changing Home invalidates plan approval and compiled mission evidence");
        mission.Waypoints.First().Navigation = new(PlannedHome: home with { Latitude = 91 });
        Check(!MissionCommandCompiler.Compile(mission, options).CanEncode, "invalid Planned Home cannot be compiled");
        mission.Waypoints.First().Navigation = null;
        var result = MissionCommandCompiler.Compile(mission, options);
        Check(result.CanEncode && !result.CanUpload && !result.CanStart && result.State == "PreviewOnly", "valid compilation remains non-executable pending runtime and policy gates");
        Check(result.Items.Select(i => i.Command).SequenceEqual(new ushort[] { 22, 178, 16, 16, 21 }), "compiler maps takeoff, ground speed, navigation, hover and landing");
        Check(result.Items.Select(i => (int)i.Seq).SequenceEqual(Enumerable.Range(0, 5)), "wire sequence is generated independently from web waypoint sequence");
        Check(result.Items[0].X == 108231000 && result.Items[0].Y == 1066297000 && result.Items[0].Frame == 3, "MISSION_ITEM_INT encodes E7 with modern non-INT relative frame");
        Check(result.Items[1] is { Command: 178, Frame: 2, Param1: 1, Param2: 5, Param3: -1 }, "cruise speed command uses ground speed without changing throttle");
        Check(result.Items[3].Param1 == 5 && result.Items[3].Param2 == 2 && result.Items[3].Param4 == null, "hover and acceptance radius are explicit; autopilot yaw is NaN sentinel rather than zero heading");
        Check(result.Items[^1].Z == 0 && mission.Waypoints.Last().Altitude == 30, "landing ground altitude is an explicit compiler option and does not mutate the approved source plan");
        Check(result.Items.All(i => i.Current == 0 && i.Autocontinue == 1 && i.MissionType == 0 && i.Command is not (400 or 300)), "mission preview contains no ARM or MISSION_START command");
        Check(result.CompiledMissionHash == MissionCommandCompiler.Compile(mission, options).CompiledMissionHash, "same plan and compiler options produce a deterministic hash");
        Check(result.CompiledMissionHash != MissionCommandCompiler.Compile(mission, options with { AcceptanceRadiusMeters = 3 }).CompiledMissionHash, "compiler policy changes invalidate the compiled hash");
        var px4 = MissionCommandCompiler.Compile(mission, options with { FirmwareTarget = "PX4" });
        Check(px4.CanEncode && px4.CompiledMissionHash != result.CompiledMissionHash && !px4.CanUpload, "PX4 has a separately identified preview and still requires its runtime adapter checks");
        var missing = MissionCommandCompiler.Compile(mission, new MissionCompileOptions("ArduPilot"));
        Check(!missing.CanEncode && missing.Items.Count == 0 && missing.CompiledMissionHash == null, "unresolved radius, hover and landing input never creates a partial executable plan");
        var modern = new FirmwareParameterSnapshot("4.7.1", new() { ["WP_RADIUS_M"] = 2, ["WP_SPD"] = 3 }, DateTimeOffset.UtcNow);
        var modernPlan = MissionCommandCompiler.Compile(mission, options with { AcceptanceRadiusMeters = null }, modern);
        Check(modernPlan.CanEncode && modernPlan.Items.Single(i => i.Command == 178).Param2 == 3
            && modernPlan.Items.Where(i => i.Command == 16).All(i => i.Param2 == 2), "ArduCopter 4.7 uses metre/second speed and metre radius without centimetre conversion");
        var legacy = new FirmwareParameterSnapshot("4.6.3", new() { ["WPNAV_RADIUS"] = 200, ["WPNAV_SPEED"] = 300 }, DateTimeOffset.UtcNow);
        var legacyPlan = MissionCommandCompiler.Compile(mission, options with { AcceptanceRadiusMeters = null }, legacy);
        Check(legacyPlan.CanEncode && legacyPlan.Items.Single(i => i.Command == 178).Param2 == 3
            && legacyPlan.Items.Where(i => i.Command == 16).All(i => i.Param2 == 2), "legacy centimetre speed and radius remain supported");
        mission.AltitudeReference = "MSL";
        Check(MissionCommandCompiler.Compile(mission, options).Items[0].Frame == 0, "MSL uses global frame with integer coordinate encoding");
        mission.AltitudeReference = "AGL";
        Check(!MissionCommandCompiler.Compile(mission, options).CanEncode, "AGL is not silently interpreted as relative altitude");
        mission.AltitudeReference = "Relative";
        Check(!MissionCommandCompiler.Compile(mission, options with { VehicleClass = "FixedWing" }).CanEncode, "unsupported airframe cannot reuse a multicopter compiler");
        Check(!MissionCommandCompiler.Compile(mission, options with { HoverDurationSeconds = null }).CanEncode, "hover duration cannot be guessed from an action name");
        mission.Waypoints.ElementAt(1).ActionType = "CapturePhoto";
        Check(!MissionCommandCompiler.Compile(mission, options).CanEncode, "deferred camera action blocks compilation");
        mission.Waypoints.ElementAt(1).ActionType = "FlyThrough";
        mission.Waypoints.ElementAt(1).Latitude = 91;
        Check(!MissionCommandCompiler.Compile(mission, options).CanEncode, "out-of-range coordinates are rejected before E7 conversion");
    }

    private static Waypoint Point(int sequence, string action) => new() { Id = Guid.NewGuid(), SequenceOrder = sequence,
        ActionType = action, Latitude = 10.8231m, Longitude = 106.6297m, Altitude = 30 };
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("FAIL: " + message);
        Console.WriteLine("PASS: " + message);
    }
}
