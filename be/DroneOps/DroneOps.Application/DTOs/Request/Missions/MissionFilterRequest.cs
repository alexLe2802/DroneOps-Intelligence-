namespace DroneOps.Application.DTOs.Request.Missions;

public sealed class MissionFilterRequest
{
    public string? Search { get; set; }

    public string? Status { get; set; }

    public DateTime? FromDate { get; set; }

    public DateTime? ToDate { get; set; }

    public int PageNumber { get; set; } = 1;

    public int PageSize { get; set; } = 10;
}