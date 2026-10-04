using DroneOps.Application.Interfaces.Auth;
using DroneOps.Application.Interfaces.Geofences;
using DroneOps.Application.Interfaces.Missions;
using DroneOps.Application.Interfaces.UAVs;
using DroneOps.Application.Interfaces.Users;
using DroneOps.Application.Services;
using DroneOps.Application.Services.Auth;
using DroneOps.Application.Services.UAVs;
using DroneOps.Application.Services.Users;
using DroneOps.Application.Validators.Users;
using DroneOps.Persistence.Repositories;
using FluentValidation;
using FluentValidation.AspNetCore;
using Microsoft.Extensions.DependencyInjection;

namespace DroneOps.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplicationServices(
        this IServiceCollection services)
    {
        services.AddMemoryCache();

        services.AddFluentValidationAutoValidation();

        services.AddValidatorsFromAssemblyContaining
            <CreatePilotRegistrationValidator>();

        services.AddScoped<IEmailService, EmailService>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IUserService, UserService>();
        services.AddScoped<IPilotRegistrationService, PilotRegistrationService>();
        services.AddScoped<IUavService, UavService>();
        services.AddScoped<IGeofenceService, GeofenceService>();
        services.AddScoped<IMissionService, MissionService>();

        return services;
    }
}