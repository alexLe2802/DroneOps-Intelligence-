namespace DroneOps.Application.DTOs.Response.Geofences;

public sealed class GeofenceCheckResult
{
    public bool IsRestricted { get; set; }

    public Guid? GeofenceId { get; set; }

    public string? GeofenceName { get; set; }

    public string? Message { get; set; }
}