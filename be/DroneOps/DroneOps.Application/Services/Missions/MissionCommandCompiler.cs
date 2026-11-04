using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DroneOps.Application.Validators.Missions;
using DroneOps.Domain.Entities;

namespace DroneOps.Application.Services.Missions;

// Explicit authoring choices. They are not silently taken from FC defaults or
// treated as approved flight policy. Runtime vehicle evidence is a separate gate.
public sealed record MissionCompileOptions(string FirmwareTarget, string VehicleClass = "Multicopter",
    decimal? AcceptanceRadiusMeters = null, string? YawPolicy = null, decimal? YawDegrees = null,
    decimal? HoverDurationSeconds = null, decimal? LandingGroundAltitudeMeters = null);

public sealed record MissionWireItem(ushort Seq, ushort Command, byte Frame,
    float Param1, float Param2, float Param3, float? Param4, int X, int Y, float Z,
    byte Current = 0, byte Autocontinue = 1, byte MissionType = 0);

public sealed record MissionCompilationPreview(string CompilerVersion, string FirmwareTarget,
    string SourcePlanHash, string? CompiledMissionHash, string State, bool CanEncode,
    bool CanUpload, bool CanStart, MissionCompileOptions Options,
    IReadOnlyList<MissionWireItem> Items, IReadOnlyList<string> Errors, IReadOnlyList<string> ExecutionBlocks);

public static class MissionCommandCompiler
{
    private const string Version = "multicopter-mission-int-v1";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static MissionCompilationPreview Compile(Mission mission, MissionCompileOptions options)
    {
        var sourceHash = MissionValidationService.Hash(mission);
        var errors = new List<string>();
        var executionBlocks = new[] {
            "Failsafe policy and compiler options must be approved and bound to the compiled hash.",
            "Detect and verify hardware UID, connection lease, firmware version, vehicle type and capabilities.",
            "Resolve home/altitude evidence and firmware-specific home/sequence conventions before upload.",
            "Upload ACK, canonical read-back and current compiled hash confirmation are not implemented yet.",
            "Fresh pre-arm checks, landed/armed state, battery sufficiency, mission deadline and pilot presence are required.",
            "Aircraft command execution is disabled; this preview cannot upload, arm or start a mission."
        };
        if (options.FirmwareTarget is not ("ArduPilot" or "PX4")) errors.Add("Select ArduPilot or PX4.");
        if (options.VehicleClass != "Multicopter") errors.Add("This compiler phase supports multicopter previews only; fixed-wing/VTOL need separate profiles.");
        if (options.AcceptanceRadiusMeters is null or <= 0) errors.Add("An explicit positive waypoint acceptance radius is required.");
        if (options.YawPolicy is not ("Autopilot" or "Fixed")) errors.Add("Select an explicit yaw policy: Autopilot or Fixed.");
        if (options.YawPolicy == "Fixed" && options.YawDegrees is not (>= 0 and < 360)) errors.Add("Fixed yaw requires an angle in [0,360) degrees.");
        if (options.YawPolicy == "Autopilot" && options.YawDegrees != null) errors.Add("Autopilot yaw must not also specify a fixed angle.");
        if (!options.LandingGroundAltitudeMeters.HasValue) errors.Add("Supply the landing ground altitude in the selected frame; route altitude is not assumed to be ground level.");
        byte frame = mission.AltitudeReference switch { "Relative" => 3, "MSL" => 0, _ => 255 };
        if (frame == 255) errors.Add("Choose Relative or MSL; AGL compilation requires a terrain-capable adapter and is not enabled in this phase.");
        if (mission.UAVId is null) errors.Add("Assign the mission to a UAV.");
        if (mission.AverageSpeedMetersPerSecond is null or <= 0 || mission.MaxSpeedMetersPerSecond is null or <= 0
            || mission.AverageSpeedMetersPerSecond > mission.MaxSpeedMetersPerSecond)
            errors.Add("Positive cruise and maximum speeds are required, with cruise <= maximum.");
        var points = mission.Waypoints.OrderBy(w => w.SequenceOrder).ToList();
        if (points.Count < 2 || points.Count >= ushort.MaxValue) errors.Add("A route needs 2–65534 waypoints and space for the speed item.");
        if (!points.Select(w => w.SequenceOrder).SequenceEqual(Enumerable.Range(1, points.Count))) errors.Add("Waypoint sequence must be unique and continuous from 1.");
        static string Action(Waypoint point) => (point.ActionType ?? "FlyThrough").Trim().ToLowerInvariant();
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
            if (action == "hover" && options.HoverDurationSeconds is null or <= 0) errors.Add("Hover requires an explicit positive duration in seconds.");
        }
        if (errors.Count > 0) return new(Version, options.FirmwareTarget, sourceHash, null, "NeedsInput", false, false, false, options, [], errors.Distinct().ToArray(), executionBlocks);

        var items = new List<MissionWireItem>();
        float? yaw = options.YawPolicy == "Fixed" ? (float)options.YawDegrees!.Value : null;
        foreach (var point in points)
        {
            var action = Action(point);
            var command = (ushort)(action == "takeoff" ? 22 : action == "land" ? 21 : 16);
            var hold = action == "hover" ? (float)options.HoverDurationSeconds!.Value : 0;
            var acceptance = command == 16 ? (float)options.AcceptanceRadiusMeters!.Value : 0;
            var altitude = action == "land" ? options.LandingGroundAltitudeMeters!.Value : point.Altitude!.Value;
            items.Add(new((ushort)items.Count, command, frame, hold, acceptance, 0, yaw,
                E7(point.Latitude), E7(point.Longitude), (float)altitude));
            if (action == "takeoff")
                items.Add(new((ushort)items.Count, 178, 2, 1, (float)mission.AverageSpeedMetersPerSecond!.Value, -1, 0, 0, 0, 0));
        }
        // Float fields are stored as float32, with null representing MAVLink NaN
        // for yaw. Binding the source hash and options prevents silent re-compilation.
        var canonical = JsonSerializer.Serialize(new { compilerVersion = Version, sourcePlanHash = sourceHash, options, items }, Json);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
        return new(Version, options.FirmwareTarget, sourceHash, hash, "PreviewOnly", true, false, false, options, items, [], executionBlocks);
    }

    private static int E7(decimal degrees) => (int)decimal.Round(degrees * 10000000m, 0, MidpointRounding.AwayFromZero);
}
