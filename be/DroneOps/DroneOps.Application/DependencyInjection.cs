using DroneOps.Application.Interfaces.Auth;
using DroneOps.Application.Interfaces.Users;
using DroneOps.Application.Services.Auth;
using DroneOps.Application.Services.Users;
using DroneOps.Application.Validators.Users;
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

        return services;
    }
}