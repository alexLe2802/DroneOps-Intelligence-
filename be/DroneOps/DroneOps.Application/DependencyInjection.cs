using DroneOps.Application.Interfaces.AI;
using DroneOps.Application.Interfaces.Auth;
using DroneOps.Application.Interfaces.Geofences;
using DroneOps.Application.Interfaces.Missions;
using DroneOps.Application.Interfaces.PostFlight;
using DroneOps.Application.Interfaces.Telemetry;
using DroneOps.Application.Interfaces.UAVs;
using DroneOps.Application.Interfaces.Users;
using DroneOps.Application.Services;
using DroneOps.Application.Services.Auth;
using DroneOps.Application.Services.Missions;
using DroneOps.Application.Services.PostFlight;
using DroneOps.Application.Services.Telemetry;
using DroneOps.Application.Services.UAVs;
using DroneOps.Application.Services.Users;
using DroneOps.Application.Validators.Users;
using DroneOps.Persistence.Interfaces;
using DroneOps.Persistence.Repositories;
using DroneOps.Persistence.Services.AI;
using FluentValidation;
using FluentValidation.AspNetCore;
using Microsoft.Extensions.DependencyInjection;
using DroneOps.Application.Interfaces.Incidents;
using DroneOps.Application.Services.Incidents;


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
        services.AddScoped<IMissionSuggestionService,MissionSuggestionService>();
        services.AddScoped<IPostFlightService, PostFlightService>();
        services.AddScoped<ITelemetryIngestionService, TelemetryIngestionService>();
        services.AddScoped<IIncidentService, IncidentService>();
        services.AddScoped<IMissionApprovalService, MissionApprovalService>();
        services.AddHttpClient<
IAIService,
OpenAIService>(client =>
{
    client.BaseAddress =
    new Uri("https://api.openai.com/v1/");
});
        return services;
    }
}