using DroneOps.Application.Interfaces;
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
        // Lấy cùng một chuỗi kết nối được dùng để tạo
        // đối tượng NpgsqlDataSource dùng chung của ứng dụng.
        var connectionString = configuration
            .GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException(
                "DefaultConnection is missing.");

        // Sử dụng một thể hiện NpgsqlDataSource dùng chung duy nhất trong toàn ứng dụng.
        //
        // Điều này rất quan trọng vì Health check, các lệnh database của Auth
        // và EF Core giờ đây sẽ dùng chung cấu hình PostgreSQL và chung Connection Pool.
        services.AddDbContext<AppDbContext>((serviceProvider, options) =>
        {
            var dataSource = serviceProvider
                .GetRequiredService<NpgsqlDataSource>();

            options.UseNpgsql(
                dataSource,
                npgsqlOptions =>
                {
                    // Tự động thử kết nối lại khi gặp sự cố mạng/kết nối PostgreSQL tạm thời.
                    npgsqlOptions.EnableRetryOnFailure(
                        maxRetryCount: 3,
                        maxRetryDelay: TimeSpan.FromSeconds(5),
                        errorCodesToAdd: null);
                });
        });

        // Đăng ký Generic Repository.
        services.AddScoped(
            typeof(IGenericRepository<>),
            typeof(GenericRepository<>));

        // Đăng ký User Repository.
        services.AddScoped<IUserRepository, UserRepository>();

        // Đăng ký Pilot Registration Repository.
        services.AddScoped<
            IPilotRegistrationRepository,
            PilotRegistrationRepository>();

        return services;
    }
}