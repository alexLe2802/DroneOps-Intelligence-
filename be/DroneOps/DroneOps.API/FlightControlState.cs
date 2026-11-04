using DroneOps.Application.Services.Missions;

namespace DroneOps.API;

// Persisted transitions. No HTTP timer or observed manual RC arming can launch a mission.
public sealed class FlightControlState
{
    public MissionCompileOptions? Options { get; set; }
    public string? PlanHash { get; set; }
    public string? CompiledHash { get; set; }
    public Guid? ConnectionId { get; set; }
    public Guid? GatewayInstance { get; set; }
    public FlightControllerEvidence? Evidence { get; set; }
    public DateTimeOffset? EvidenceAt { get; set; }
    public DateTimeOffset? PilotAt { get; set; }
    public string Stage { get; set; } = "NeedsPlan";
    public string Message { get; set; } = "Save explicit FC mission settings before upload.";
    public bool UploadVerified { get; set; }
    public DateTimeOffset? LaunchAt { get; set; }
    public List<FlightControlCommand> Commands { get; set; } = [];
    [System.Text.Json.Serialization.JsonIgnore]
    public FlightControlCommand? Pending => Commands.LastOrDefault(c => c.Result == null);

    public bool Fresh(DateTimeOffset now) => EvidenceAt is { } seen && seen <= now && now - seen <= TimeSpan.FromSeconds(3);
    public bool OnGround(DateTimeOffset now) => Fresh(now) && Evidence?.LandedState == 1;
    public bool PilotPresent(DateTimeOffset now) => PilotAt is { } seen && seen <= now && now - seen <= TimeSpan.FromSeconds(3);

    public void Cancel(string reason)
    {
        if (Stage is "Starting" or "Running") return; // No promise to undo an already-issued start.
        if (Pending is { } command) command.Result = command.DeliveredAt == null ? "Cancelled" : "Unknown";
        LaunchAt = null;
        Stage = "Cancelled";
        Message = reason + " Motors are not automatically disarmed.";
    }

    public void Tick(DateTimeOffset now, bool authorized)
    {
        if (Stage is "Arming" or "Countdown")
        {
            if (!authorized || !Fresh(now) || !OnGround(now) || !PilotPresent(now) || Evidence?.NavigationReady != true)
                Cancel("Launch cancelled because readiness, pilot presence or the connection changed.");
            else if (Stage == "Countdown" && Evidence.Armed != true)
                Cancel("Launch cancelled because FC is no longer armed.");
        }
        if (Pending is { } pending && now >= pending.ExpiresAt)
        {
            pending.Result = pending.DeliveredAt == null ? "Expired" : "Unknown";
            Stage = "Unknown"; LaunchAt = null;
            Message = "Command outcome is unknown. Check FC status; commands are never replayed automatically.";
        }
    }

    public FlightControlCommand Queue(string action, DateTimeOffset now, Guid? operationId = null)
    {
        if (Pending != null) throw new InvalidOperationException("A flight command is already pending.");
        var command = new FlightControlCommand { Id = operationId ?? Guid.NewGuid(), Action = action, CreatedAt = now,
            ExpiresAt = now.AddSeconds(action == "Upload" ? 60 : 8) };
        Commands.Add(command);
        if (Commands.Count > 32) Commands.RemoveAt(0);
        Stage = action switch { "Upload" => "Uploading", "Arm" => "Arming", "Disarm" => "Disarming", "Start" => "Starting", _ => throw new InvalidOperationException("Unsupported command.") };
        Message = "Waiting for FC: " + action;
        return command;
    }

    public void Complete(Guid commandId, bool succeeded, string? uploadedHash, DateTimeOffset now)
    {
        var command = Pending;
        if (command == null || command.Id != commandId || command.DeliveredAt == null || now >= command.ExpiresAt) return;
        if (!succeeded)
        {
            command.Result = "Failed"; Stage = "Failed"; LaunchAt = null;
            Message = "FC rejected the command or verification failed. Review the aircraft before trying again.";
            return;
        }
        var verified = command.Action switch {
            "Upload" => OnGround(now) && Evidence?.Armed == false && uploadedHash == CompiledHash,
            "Arm" => OnGround(now) && Evidence?.Armed == true && PilotPresent(now),
            "Disarm" => OnGround(now) && Evidence?.Armed == false,
            "Start" => Fresh(now) && Evidence?.Armed == true,
            _ => false
        };
        command.Result = verified ? "Succeeded" : "Unknown";
        if (!verified) { Stage = "Unknown"; LaunchAt = null; Message = "FC acknowledgement and observed aircraft state do not match."; return; }
        switch (command.Action)
        {
            case "Upload": UploadVerified = true; Stage = "Ready"; Message = "Mission uploaded and read-back verified on FC."; break;
            case "Arm": Stage = "Countdown"; LaunchAt = now.AddSeconds(10); Message = "FC confirmed ARM. Takeoff countdown started."; break;
            case "Disarm": Stage = "Ready"; LaunchAt = null; Message = "FC confirmed DISARM."; break;
            case "Start": Stage = "Running"; LaunchAt = null; Message = "FC accepted mission start. Monitor flight telemetry."; break;
        }
    }
}

public sealed record FlightControllerEvidence(Guid GatewayInstance, string? Firmware, bool? Armed,
    int? LandedState, bool NavigationReady, bool PreArmReady, string? UploadedHash);

public sealed class FlightControlCommand
{
    public Guid Id { get; set; }
    public string Action { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? DeliveredAt { get; set; }
    public string? Result { get; set; }
}
