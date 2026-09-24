using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;

namespace DroneOps.API.HealthChecks;

public sealed class DatabaseHealthCheck(NpgsqlDataSource dataSource) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var command = dataSource.CreateCommand("SELECT 1");
            command.CommandTimeout = 5;
            var result = await command.ExecuteScalarAsync(cancellationToken);
            return result is 1
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy("Database probe failed.");
        }
        catch (Exception exception) when (exception is NpgsqlException or TimeoutException or OperationCanceledException)
        {
            // Do not expose credentials, host details or provider errors through health responses/logs.
            return HealthCheckResult.Unhealthy("Database unavailable.");
        }
    }
}
