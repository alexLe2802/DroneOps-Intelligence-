using DroneOps.Application.DTOs.Request.Users;
using DroneOps.Application.DTOs.Response.Users;

namespace DroneOps.Application.Interfaces.Users;

public interface IUserService
{
    Task<UserProfileResponse?> GetProfileAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<UserProfileResponse?> UpdateProfileAsync(
        Guid userId,
        UpdateProfileRequest request,
        CancellationToken cancellationToken = default);
}