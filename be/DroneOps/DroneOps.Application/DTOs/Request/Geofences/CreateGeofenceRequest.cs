namespace DroneOps.Application.DTOs.Request.Geofences;

public class CreateGeofenceRequest
{
    public string RestrictionType { get; set; } = "Restricted";
    public string VerticalRule { get; set; } = "Unspecified";
    public string? AltitudeReference { get; set; }
    public decimal? MinAltitudeMeters { get; set; }
    public decimal? MaxAltitudeMeters { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public List<GeofenceCoordinateRequest> Coordinates { get; set; }
        = new();
}
