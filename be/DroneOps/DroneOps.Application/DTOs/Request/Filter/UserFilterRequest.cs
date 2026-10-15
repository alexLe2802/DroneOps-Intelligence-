namespace DroneOps.Application.DTOs.Request.Users;

public sealed class UserFilterRequest
{
    public int PageNumber { get; set; } = 1;

    public int PageSize { get; set; } = 10;

    public string? Search { get; set; }

    public string? Role { get; set; }
}