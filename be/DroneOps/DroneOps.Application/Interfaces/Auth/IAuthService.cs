using DroneOps.Application.DTOs.Auth;

namespace DroneOps.Application.Interfaces.Auth;

public interface IAuthService
{
    Task<LoginResponse?> LoginAsync(
        LoginRequest request,
        CancellationToken cancellationToken = default);

    Task<string> RegisterAsync(
        RegisterRequest request,
        CancellationToken cancellationToken = default);

    Task<bool> VerifyRegisterAsync(
        VerifyRegisterRequest request,
        CancellationToken cancellationToken = default);
}