namespace DroneOps.Application.DTOs.Response.Geofences;

public class GeofenceResponse
{
    public string VerticalRule { get; set; } = "Unspecified";
    public string? AltitudeReference { get; set; }
    public decimal? MinAltitudeMeters { get; set; }
    public decimal? MaxAltitudeMeters { get; set; }
    public long Revision { get; set; }

    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public List<GeofenceCoordinateResponse> Coordinates { get; set; }
        = new();

    public DateTime CreatedAt { get; set; }
}