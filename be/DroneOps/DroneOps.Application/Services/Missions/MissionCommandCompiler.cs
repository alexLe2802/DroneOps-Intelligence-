using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DroneOps.Application.Validators.Missions;
using DroneOps.Domain.Entities;

namespace DroneOps.Application.Services.Missions;

// Explicit authoring choices. They are not silently taken from FC defaults or
// treated as approved flight policy. Runtime vehicle evidence is a separate gate.
public sealed record MissionCompileOptions(string FirmwareTarget, string VehicleClass = "Multicopter",
    decimal? AcceptanceRadiusMeters = null, string? YawPolicy = "Autopilot", decimal? YawDegrees = null,
    decimal? HoverDurationSeconds = null, decimal? LandingGroundAltitudeMeters = null, bool ConfigureRecovery = false);

public sealed record MissionWireItem(ushort Seq, ushort Command, byte Frame,
    float Param1, float Param2, float Param3, float? Param4, int X, int Y, float Z,
    byte Current = 0, byte Autocontinue = 1, byte MissionType = 0);

public sealed record MissionCompilationPreview(string CompilerVersion, string FirmwareTarget,
    string SourcePlanHash, string? CompiledMissionHash, string State, bool CanEncode,
    bool CanUpload, bool CanStart, MissionCompileOptions Options,
    IReadOnlyList<MissionWireItem> Items, IReadOnlyList<string> Errors, IReadOnlyList<string> ExecutionBlocks,
    AutomaticExecutionEnvelope? OperationalEnvelope = null, PlannedHome? PlannedHome = null, IReadOnlyList<int>? WaypointCommands = null);

public sealed record AutomaticExecutionEnvelope(decimal CruiseSpeedMetersPerSecond, decimal MaxSpeedMetersPerSecond,
    AutomaticFlightLimits Limits, bool FcParameterVerificationRequired = true, bool ConfigureRecovery = false);

public static class MissionCommandCompiler
{
    private const string Version = "multicopter-mission-int-v3";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static MissionCompilationPreview Compile(Mission mission, MissionCompileOptions options, FirmwareParameterSnapshot? firmwareDefaults = null)
    {
        var sourceHash = MissionValidationService.Hash(mission);
        var errors = new List<string>();
        if (mission.AutomaticFlightLimits is { } recoveryLimits)
            errors.AddRange(RecoveryEnergyCalculator.ProfileErrors(recoveryLimits.RecoveryEnergyProfile, recoveryLimits,
                DateTimeOffset.UtcNow, expectedUav: mission.UAVId));
        errors.AddRange(AutomaticFlightPolicy.Errors(mission.AutomaticFlightRequested, mission.AltitudeReference,
            mission.AverageSpeedMetersPerSecond ?? 0, mission.MaxSpeedMetersPerSecond ?? 0, mission.AutomaticFlightLimits,
            mission.Waypoints.Select(w => (w.Altitude, w.ActionType))));
        if (mission.AutomaticFlightRequested)
            errors.AddRange(AutomaticFlightPolicy.NavigationErrors(mission.Waypoints.Select(w => (w.Navigation, w.ActionType)), mission.MaxSpeedMetersPerSecond ?? 0));
        decimal? FcValue(string name, decimal scale = 1m) => firmwareDefaults?.Values?.TryGetValue(name, out var value) == true ? value * scale : null;
        var radius = options.AcceptanceRadiusMeters ?? (options.FirmwareTarget == "PX4" ? FcValue("NAV_ACC_RAD") : (FcValue("WP_RADIUS_M") ?? FcValue("WPNAV_RADIUS", 0.01m)));
        var executionBlocks = new[] {
            "Compiler options and optional FC recovery configuration are bound to the compiled hash.",
            "Detect and verify hardware UID, connection lease, firmware version, vehicle type and capabilities.",
            "Resolve home/altitude evidence and firmware-specific home/sequence conventions before upload.",
            "Upload ACK, canonical read-back and current compiled hash confirmation must succeed on the bound FC.",
            "Fresh pre-arm checks, landed/armed state, battery sufficiency, mission deadline and pilot presence are required.",
            "Aircraft command execution is disabled; this preview cannot upload, arm or start a mission."
        };
        if (options.FirmwareTarget is not ("ArduPilot" or "PX4")) errors.Add("Select ArduPilot or PX4.");
        if (options.VehicleClass != "Multicopter") errors.Add("This compiler phase supports multicopter previews only; fixed-wing/VTOL need separate profiles.");
        var defaultRadiusNeeded = mission.AutomaticFlightRequested || mission.Waypoints.Any(w => (w.ActionType == null || w.ActionType.Equals("FlyThrough", StringComparison.OrdinalIgnoreCase)
            || w.ActionType.Equals("Hover", StringComparison.OrdinalIgnoreCase)) && w.Navigation?.AcceptanceRadiusMeters == null);
        if (radius is <= 0 or > 10 || radius == null && defaultRadiusNeeded) errors.Add("A positive acceptance radius <= 10 m must be supplied or read from the bound FC.");
        if (options.YawPolicy is not ("Autopilot" or "Fixed")) errors.Add("Select an explicit yaw policy: Autopilot or Fixed.");
        if (options.YawPolicy == "Fixed" && options.YawDegrees is not (>= 0 and < 360)) errors.Add("Fixed yaw requires an angle in [0,360) degrees.");
        if (options.YawPolicy == "Autopilot" && options.YawDegrees != null) errors.Add("Autopilot yaw must not also specify a fixed angle.");
        byte frame = mission.AltitudeReference switch { "Relative" => 3, "MSL" => 0, _ => 255 };
        if (frame == 255) errors.Add("Choose Relative or MSL; AGL compilation requires a terrain-capable adapter and is not enabled in this phase.");
        if (mission.UAVId is null) errors.Add("Assign the mission to a UAV.");
        if (mission.AverageSpeedMetersPerSecond is null or <= 0 || mission.MaxSpeedMetersPerSecond is null or <= 0
            || mission.AverageSpeedMetersPerSecond > mission.MaxSpeedMetersPerSecond)
            errors.Add("Positive cruise and maximum speeds are required, with cruise <= maximum.");
        var points = mission.Waypoints.OrderBy(w => w.SequenceOrder).ToList();
        var plannedHome = points.FirstOrDefault()?.Navigation?.PlannedHome;
        if (points.Skip(1).Any(p => p.Navigation?.PlannedHome != null)
            || plannedHome is { } home && (home.Latitude is < -90 or > 90 || home.Longitude is < -180 or > 180 || home.AltitudeMslMeters is < -500 or > 10000))
            errors.Add("Planned Home must be on the first waypoint with valid WGS84 coordinates and MSL elevation.");
        if (points.Count < 2 || points.Count >= ushort.MaxValue) errors.Add("A route needs 2–65534 waypoints and space for the speed item.");
        if (!points.Select(w => w.SequenceOrder).SequenceEqual(Enumerable.Range(1, points.Count))) errors.Add("Waypoint sequence must be unique and continuous from 1.");
        static string Action(Waypoint point) => (point.ActionType ?? "FlyThrough").Trim().ToLowerInvariant();
        if (options.FirmwareTarget != "ArduPilot" && points.Any(p => Action(p) == "land" && p.Navigation?.LandingGroundAltitudeMeters == null && options.LandingGroundAltitudeMeters == null))
            errors.Add("Supply landing ground altitude at the LAND node or in the explicit adapter settings; route altitude is not ground level.");
        if (points.Count > 0 && (Action(points[0]) != "takeoff" || Action(points[^1]) != "land")) errors.Add("The first action must be TakeOff and the last action Land.");
        for (var index = 0; index < points.Count; index++)
        {
            var point = points[index];
            var action = Action(point);
            if (point.Latitude is < -90 or > 90 || point.Longitude is < -180 or > 180) errors.Add($"Waypoint {point.SequenceOrder} has invalid WGS84 coordinates.");
            if (action is not ("takeoff" or "flythrough" or "hover" or "land")) errors.Add($"Waypoint {point.SequenceOrder}: unsupported action; camera is deferred.");
            if (index > 0 && action == "takeoff" || index < points.Count - 1 && action == "land") errors.Add("Intermediate takeoff/landing is outside this compiler profile.");
            if (action != "land" && !point.Altitude.HasValue) errors.Add($"Waypoint {point.SequenceOrder} needs an explicit altitude.");
            if (frame == 3 && action != "land" && point.Altitude is <= 0) errors.Add($"Waypoint {point.SequenceOrder} relative altitude must be positive.");
            if (action == "hover" && (point.Navigation?.HoldTimeSeconds ?? options.HoverDurationSeconds) is null or <= 0) errors.Add("Hover requires an explicit positive duration in seconds.");
        }
        if (errors.Count > 0) return new(Version, options.FirmwareTarget, sourceHash, null, "NeedsInput", false, false, false, options, [], errors.Distinct().ToArray(), executionBlocks);

        var items = new List<MissionWireItem>();
        var waypointCommands = new List<int>();
        float? yaw = options.YawPolicy == "Fixed" ? (float)options.YawDegrees!.Value : null;
        var cruise = mission.AverageSpeedMetersPerSecond!.Value;
        var fcCruise = options.FirmwareTarget == "PX4" ? FcValue("MPC_XY_CRUISE") : FcValue("WP_SPD") ?? FcValue("WPNAV_SPEED", 0.01m);
        // Automatic missions use their explicitly approved speed. The adapter
        // verifies the new FC envelope before upload; an old WP default must
        // not silently lower that speed or change the hash after applying it.
        if ((!mission.AutomaticFlightRequested || mission.AutomaticFlightLimits?.PolicyVersion != "multicopter-envelope-v2") && fcCruise is > 0)
            cruise = Math.Min(cruise, fcCruise.Value);
        decimal currentSpeed = cruise;
        foreach (var point in points)
        {
            var action = Action(point);
            var command = (ushort)(action == "takeoff" ? 22 : action == "land" ? 21 : 16);
            var targetSpeed = point.Navigation?.SpeedMetersPerSecond ?? cruise;
            if (action != "takeoff" && targetSpeed != currentSpeed)
            {
                items.Add(new((ushort)items.Count, 178, 2, 1, (float)targetSpeed, -1, 0, 0, 0, 0));
                currentSpeed = targetSpeed;
            }
            var hold = command == 16 ? (float)(point.Navigation?.HoldTimeSeconds ?? (action == "hover" ? options.HoverDurationSeconds : null) ?? 0) : 0;
            var acceptance = command == 16 ? (float)(point.Navigation?.AcceptanceRadiusMeters ?? radius!.Value) : 0;
            // Copter LAND ignores altitude and uses its onboard landing detector.
            // Keep PX4's explicit ground-altitude contract.
            var altitude = action == "land" ? (point.Navigation?.LandingGroundAltitudeMeters ?? options.LandingGroundAltitudeMeters ?? 0) : point.Altitude!.Value;
            if (mission.AutomaticFlightRequested && action == "land")
            {
                // Reach the final waypoint at its approved altitude before descending.
                items.Add(new((ushort)items.Count, 16, frame, 0, (float)radius!.Value, 0,
                    point.Navigation?.YawDegrees is { } arrivalYaw ? (float?)arrivalYaw : yaw,
                    E7(point.Latitude), E7(point.Longitude), (float)point.Altitude!.Value));
            }
            waypointCommands.Add(items.Count + (options.FirmwareTarget == "ArduPilot" ? 1 : 0));
            var pointYaw = point.Navigation?.YawDegrees is { } angle ? (float?)angle : yaw;
            items.Add(new((ushort)items.Count, command, frame, hold, acceptance, 0, pointYaw,
                E7(point.Latitude), E7(point.Longitude), (float)altitude));
            if (action == "takeoff")
                items.Add(new((ushort)items.Count, 178, 2, 1, (float)cruise, -1, 0, 0, 0, 0));
        }
        // Float fields are stored as float32, with null representing MAVLink NaN
        // for yaw. Binding the source hash and options prevents silent re-compilation.
        var canonical = JsonSerializer.Serialize(new { compilerVersion = Version, sourcePlanHash = sourceHash, options, items, firmwareVersion = firmwareDefaults?.FirmwareVersion }, Json);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
        return new(Version, options.FirmwareTarget, sourceHash, hash, "PreviewOnly", true, false, false, options, items, [], executionBlocks,
            mission.AutomaticFlightRequested && mission.AutomaticFlightLimits is { } limits
                ? new(mission.AverageSpeedMetersPerSecond!.Value, mission.MaxSpeedMetersPerSecond!.Value, limits, ConfigureRecovery: options.ConfigureRecovery) : null, plannedHome, waypointCommands);
    }

    private static int E7(decimal degrees) => (int)decimal.Round(degrees * 10000000m, 0, MidpointRounding.AwayFromZero);
}
