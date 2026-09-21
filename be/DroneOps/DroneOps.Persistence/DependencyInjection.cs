using DroneOps.Application.Interfaces;
using DroneOps.Persistence.Data;
using DroneOps.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

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

        services.AddDbContext<AppDbContext>(options =>
        {
            options.UseNpgsql(connectionString);
        });

        services.AddScoped(
            typeof(IGenericRepository<>),
            typeof(GenericRepository<>));

        services.AddScoped<IUserRepository, UserRepository>();

        return services;
    }
}
