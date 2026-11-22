using System.Diagnostics;
using DroneOps.Persistence.Data;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace DroneOps.Persistence;

public static class DatabaseSchemaVerifier
{
    public static async Task VerifyAsync(
        NpgsqlDataSource source,
        AppDbContext db,
        ILogger? logger = null,
        CancellationToken cancellationToken = default)
    {
        var checks = new (string Name, Func<Task> Run)[]
        {
            ("mission workflow", () => MissionWorkflowSchema.CheckAsync(source)),
            ("drone connections", () => DroneConnectionSchema.CheckAsync(source)),
            ("account workflow", () => AccountWorkflowSchema.CheckAsync(source)),
            ("flight preparation", () => FlightPreparationSchema.CheckAsync(source, cancellationToken)),
            ("telemetry", () => TelemetrySchema.CheckAsync(source)),
            ("post-flight", () => PostFlightSchema.CheckAsync(source)),
            ("notifications", () => NotificationSchema.CheckAsync(source, cancellationToken)),
            ("UAV images", () => UavImageSchema.CheckAsync(source)),
            ("UAV reviews", () => UavReviewSchema.CheckAsync(source)),
            ("pilot license", () => PilotLicenseSchema.CheckAsync(source, cancellationToken)),
            ("EF UAV mapping", () => UavImageSchema.CheckMappedEntityAsync(db, cancellationToken))
        };

        var total = Stopwatch.StartNew();
        foreach (var check in checks)
        {
            var elapsed = Stopwatch.StartNew();
            await check.Run();
            logger?.LogInformation("Database schema check {SchemaCheck} completed in {ElapsedMilliseconds} ms",
                check.Name, elapsed.ElapsedMilliseconds);
        }

        logger?.LogInformation("All database schema checks completed in {ElapsedMilliseconds} ms", total.ElapsedMilliseconds);
    }
}
