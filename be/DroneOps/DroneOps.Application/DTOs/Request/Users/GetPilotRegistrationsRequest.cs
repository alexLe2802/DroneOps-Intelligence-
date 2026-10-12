namespace DroneOps.Application.DTOs.Request.Users;

public class GetPilotRegistrationsRequest
{
    public int PageNumber { get; set; } = 1;

    public int PageSize { get; set; } = 10;

    public string? Search { get; set; }

    public string? Status { get; set; }
}