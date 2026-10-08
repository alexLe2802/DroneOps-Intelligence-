namespace DroneOps.Application.DTOs.Request.Uavs;

public sealed class GetPendingUavsRequest
{
    public int PageNumber { get; set; } = 1;

    public int PageSize { get; set; } = 10;

    public string? Keyword { get; set; }
}