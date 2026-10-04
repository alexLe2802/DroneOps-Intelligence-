namespace DroneOps.Application.DTOs.Response.Geofences;

public class GeofenceResponse
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public List<GeofenceCoordinateResponse> Coordinates { get; set; }
        = new();

    public DateTime CreatedAt { get; set; }
}