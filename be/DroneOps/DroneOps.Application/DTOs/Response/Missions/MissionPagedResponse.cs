namespace DroneOps.Application.DTOs.Response.Missions;

public sealed class MissionPagedResponse
{
    public int PageNumber { get; set; }

    public int PageSize { get; set; }

    public int TotalCount { get; set; }

    public int TotalPages { get; set; }

    public List<MissionResponse> Items { get; set; } = [];
}