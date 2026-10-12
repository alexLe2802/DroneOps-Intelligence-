namespace DroneOps.Application.DTOs.Request.Missions;

public sealed class CreateWaypointRequest
{
    public int SequenceOrder { get; set; }

    public decimal Latitude { get; set; }

    public decimal Longitude { get; set; }

    public decimal? Altitude { get; set; }

    public string? ActionType { get; set; }
}