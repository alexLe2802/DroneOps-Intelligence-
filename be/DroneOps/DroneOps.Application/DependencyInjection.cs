using DroneOps.Application.Interfaces;
using DroneOps.Application.Interfaces.Users;
using DroneOps.Application.Services;
using DroneOps.Application.Services.Users;
using Microsoft.Extensions.DependencyInjection;

namespace DroneOps.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplicationServices(
        this IServiceCollection services)
    {
        // Đăng ký bộ nhớ đệm lưu mã OTP
        services.AddMemoryCache();

        // Đăng ký dịch vụ gửi Mail và Xác thực
        services.AddScoped<IEmailService, EmailService>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IUserService, UserService>();
        return services;
    }
}