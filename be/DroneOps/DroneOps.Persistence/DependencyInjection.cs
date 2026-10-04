using DroneOps.Application.Interfaces;
using DroneOps.Application.Interfaces.Geofences;
using DroneOps.Application.Interfaces.Missions;
using DroneOps.Persistence.Data;
using DroneOps.Persistence.Interfaces;
using DroneOps.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace DroneOps.Persistence;

public static class DependencyInjection
{
    public static IServiceCollection AddPersistenceServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration
            .GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException(
                "DefaultConnection is missing.");
        services.AddDbContext<AppDbContext>((serviceProvider, options) =>
        {
            var dataSource = serviceProvider
                .GetRequiredService<NpgsqlDataSource>();

            options.UseNpgsql(
                dataSource,
                npgsqlOptions =>
                {
                    npgsqlOptions.EnableRetryOnFailure(
                        maxRetryCount: 3,
                        maxRetryDelay: TimeSpan.FromSeconds(5),
                        errorCodesToAdd: null);
                });
        });

        
        services.AddScoped(
            typeof(IGenericRepository<>),
            typeof(GenericRepository<>));

    
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IPilotRepository, PilotRepository>();
        services.AddScoped<IPilotRegistrationRepository,PilotRegistrationRepository>();
        services.AddScoped<IUavRepository,UavRepository>();
        services.AddScoped<IMissionRepository, MissionRepository>();
        services.AddScoped<IGeofenceRepository, GeofenceRepository>();
       
        return services;
    }
}