using DroneOps.Application.DTOs.Request.Users;
using DroneOps.Application.DTOs.Response.Pages;
using DroneOps.Application.DTOs.Response.Users;
using BCrypt.Net;
namespace DroneOps.Application.Interfaces.Users;

public interface IPilotRegistrationService
{
    Task RegisterAsync(
        CreatePilotRegistrationRequest request);

    Task<PagedPilotRegistrationResponse> GetAllAsync(
        GetPilotRegistrationsRequest request);

    Task<PilotRegistrationDetailResponse?> GetDetailAsync(
        Guid id);
}