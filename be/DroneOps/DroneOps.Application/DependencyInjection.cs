using DroneOps.Application.Interfaces;
using DroneOps.Application.Services;
using Microsoft.Extensions.DependencyInjection;

namespace DroneOps.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplicationServices(
        this IServiceCollection services)
    {
        services.AddScoped<IAuthService, AuthService>();

        return services;
    }
}