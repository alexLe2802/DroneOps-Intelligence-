namespace DroneOps.Application.DTOs.Request.UAV;

public sealed class GetAllUavsRequest
{
    public int PageNumber { get; set; } = 1;

    public int PageSize { get; set; } = 10;

    public string? Keyword { get; set; }

    public string? Status { get; set; }
}