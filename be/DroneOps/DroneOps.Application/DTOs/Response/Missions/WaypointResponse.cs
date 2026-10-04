namespace DroneOps.Application.DTOs.Response.Missions;

public sealed class WaypointResponse
{
    public Guid Id { get; set; }

    public int SequenceOrder { get; set; }

    public decimal Latitude { get; set; }

    public decimal Longitude { get; set; }

    public decimal? Altitude { get; set; }

    public string? ActionType { get; set; }
}