namespace DroneOps.Application.DTOs.Request.Geofences;

public class UpdateGeofenceRequest
{
    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public List<GeofenceCoordinateRequest> Coordinates { get; set; }
        = new();
}