using DroneOps.Application.DTOs.Response.Users;

namespace DroneOps.Application.DTOs.Response.Pages;

public class PagedPilotRegistrationResponse
{
    public List<PilotRegistrationResponse> Items { get; set; } = [];

    public int PageNumber { get; set; }

    public int PageSize { get; set; }

    public int TotalItems { get; set; }

    public int TotalPages { get; set; }
}