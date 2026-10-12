namespace DroneOps.Application.DTOs.Request.Geofences;

public class CreateGeofenceRequest
{
    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public List<GeofenceCoordinateRequest> Coordinates { get; set; }
        = new();
}