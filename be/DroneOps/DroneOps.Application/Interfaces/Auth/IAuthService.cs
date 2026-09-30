using DroneOps.Application.DTOs.Request.Auth;
using DroneOps.Application.DTOs.Response.Auth;

namespace DroneOps.Application.Interfaces.Auth;

public interface IAuthService
{
    // Đăng nhập hệ thống cấp AccessToken
    Task<LoginResponse?> LoginAsync(
        LoginRequest request,
        CancellationToken cancellationToken = default);

    // 1. Đăng ký tài khoản Admin (gán Role: Admin)
    Task<string> RegisterAsync(
        RegisterRequest request,
        CancellationToken cancellationToken = default);

    // 2. Đăng ký tài khoản Phi công (gán Role: Pilot)
    Task<string> RegisterPilotAsync(
        RegisterRequest request,
        CancellationToken cancellationToken = default);

    // 3. Xác thực OTP 5 ký tự chung (tự động nhận biết Admin hay Pilot từ Cache)
    Task<bool> VerifyRegisterAsync(
        VerifyRegisterRequest request,
        CancellationToken cancellationToken = default);
}