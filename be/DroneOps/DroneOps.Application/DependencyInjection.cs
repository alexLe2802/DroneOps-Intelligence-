using DroneOps.Application.DTOs.Options;
using DroneOps.Application.Interfaces;
using DroneOps.Application.Interfaces.AI;
using DroneOps.Application.Interfaces.Auth;
using DroneOps.Application.Interfaces.Geofences;
using DroneOps.Application.Interfaces.Incidents;
using DroneOps.Application.Interfaces.Missions;
using DroneOps.Application.Interfaces.Notifications;
using DroneOps.Application.Interfaces.PostFlight;
using DroneOps.Application.Interfaces.Services;
using DroneOps.Application.Interfaces.Storage;
using DroneOps.Application.Interfaces.Telemetry;
using DroneOps.Application.Interfaces.UAVs;
using DroneOps.Application.Interfaces.Users;
using DroneOps.Application.Interfaces.Validators;
using DroneOps.Application.Services;
using DroneOps.Application.Services.Auth;
using DroneOps.Application.Services.AI;
using DroneOps.Application.Services.Incidents;
using DroneOps.Application.Services.Missions;
using DroneOps.Application.Services.Notifications;
using DroneOps.Application.Services.PostFlight;
using DroneOps.Application.Services.Storage;
using DroneOps.Application.Services.Telemetry;
using DroneOps.Application.Services.UAVs;
using DroneOps.Application.Services.Users;
using DroneOps.Application.Settings;
using DroneOps.Application.Validators.Missions;
using DroneOps.Application.Validators.Users;
using DroneOps.Persistence.Interfaces;
using DroneOps.Persistence.Repositories;
using DroneOps.Persistence.Services.AI;
using FluentValidation;
using FluentValidation.AspNetCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;


namespace DroneOps.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplicationServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<TelemetryMonitoringOptions>(
            configuration.GetSection(TelemetryMonitoringOptions.SectionName));
        services.Configure<PostFlightOptions>(
            configuration.GetSection(PostFlightOptions.SectionName));
        services.AddMemoryCache();

        services.AddFluentValidationAutoValidation();

        services.AddValidatorsFromAssemblyContaining
            <CreatePilotRegistrationValidator>();

        services.AddScoped<MissionBusinessValidator>();
        services.AddScoped<IMissionValidationService, MissionValidationService>();
        services.AddScoped<IMissionWorkflowService, MissionWorkflowService>();
        services.AddScoped<IMissionWeatherQueryService,MissionWeatherQueryService>();

        services.AddOptions<MissionWeatherOptions>();

        services.AddScoped<IEmailService, EmailService>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IUserService, UserService>();
        services.AddScoped<IPilotRegistrationService, PilotRegistrationService>();
        services.AddScoped<IUavService, UavService>();
        services.AddHttpClient<IUavImageModerationService, UavImageModerationService>(client =>
        {
            client.BaseAddress = new Uri("https://api.openai.com/v1/");
            client.Timeout = TimeSpan.FromSeconds(15);
        });
        services.AddScoped<IGeofenceService, GeofenceService>();
        services.AddScoped<IMissionService, MissionService>();
        services.AddScoped<IMissionSuggestionService,MissionSuggestionService>();
        services.AddScoped<IPostFlightService, PostFlightService>();
        services.AddScoped<ITelemetryIngestionService, TelemetryIngestionService>();
        services.AddScoped<IDroneConnectionRepository, DroneConnectionRepository>();
        services.AddScoped<IDroneConnectionService, DroneConnectionService>();
        services.AddScoped<IIncidentService, IncidentService>();
        services.AddScoped<IIncidentLifecycleService, IncidentLifecycleService>();
        services.AddScoped<IAiAssessmentService, AiAssessmentService>();
        services.AddScoped<IMissionApprovalService, MissionApprovalService>();
        services.AddScoped<INotificationService, NotificationService>();
        services.AddScoped<IAvatarService, AvatarService>();
        services.AddScoped<IUserMediaStorage, UserMediaStorage>();
        services.AddScoped<IOperatorCredentialsService,OperatorCredentialsService>();
        services.AddScoped<IPilotUpdateRequestService, PilotUpdateRequestService>();
        services.AddScoped<IMissionPreflightService,MissionPreflightService>();

        services.AddHttpClient<
    IMissionWeatherService,
    MissionWeatherService>(
        client =>
        {
            client.Timeout =
                TimeSpan.FromSeconds(15);
        });

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
