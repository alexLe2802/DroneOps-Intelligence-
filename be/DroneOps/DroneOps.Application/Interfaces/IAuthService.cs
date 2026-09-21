using DroneOps.Application.DTOs.Auth;

namespace DroneOps.Application.Interfaces;

public interface IAuthService
{
    Task<LoginResponse?> LoginAsync(
        LoginRequest request,
        CancellationToken cancellationToken = default);
}