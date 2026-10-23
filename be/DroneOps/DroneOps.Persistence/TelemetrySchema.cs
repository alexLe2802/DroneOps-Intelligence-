using Npgsql;

namespace DroneOps.Persistence;

public static class TelemetrySchema
{
    public static async Task MigrateAsync(NpgsqlDataSource source)
    {
        await ApplyEmbeddedAsync(source, "DroneOps.Persistence.Migrations.005_nullable_live_telemetry.sql");
        await ApplyEmbeddedAsync(source, "DroneOps.Persistence.Migrations.008_realtime_incident_lifecycle.sql");
        await CheckAsync(source);
    }

    public static async Task CheckAsync(NpgsqlDataSource source)
    {
        // LIMIT 0 checks the new telemetry columns without reading flight data.
        await using var command = source.CreateCommand("""
            SELECT t."HeadingDegrees", t."BatteryVoltageVolts", t."BoardTemperatureCelsius",
                   transition."EventType", alert."ReadAt"
            FROM "TelemetryRecord" t
            LEFT JOIN "IncidentTransition" transition ON FALSE
            LEFT JOIN "IncidentAlertDelivery" alert ON FALSE
            LIMIT 0
            """);
        try
        {
            await using var reader = await command.ExecuteReaderAsync();
        }
        catch (PostgresException ex) when (ex.SqlState is "42703" or "42P01")
        {
            throw new InvalidOperationException(
                "Telemetry schema is out of date. Run the API with --migrate-telemetry " +
                "against this database before starting it, or apply " +
                "database/migrations/005_nullable_live_telemetry.sql and " +
                "database/migrations/008_realtime_incident_lifecycle.sql. " +
                "The base application schema must already exist.", ex);
        }
    }

    private static async Task ApplyEmbeddedAsync(NpgsqlDataSource source, string resourceName)
    {
        using var stream = typeof(TelemetrySchema).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded migration '{resourceName}' is missing.");
        using var reader = new StreamReader(stream);
        await using var command = source.CreateCommand(await reader.ReadToEndAsync());
        await command.ExecuteNonQueryAsync();
    }
}
