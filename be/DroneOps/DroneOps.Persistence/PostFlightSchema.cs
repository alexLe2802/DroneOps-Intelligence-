using Npgsql;

namespace DroneOps.Persistence;

public static class PostFlightSchema
{
    public static async Task MigrateAsync(NpgsqlDataSource source)
    {
        using var stream = typeof(PostFlightSchema).Assembly.GetManifestResourceStream(
            "DroneOps.Persistence.Migrations.006_post_flight_integrity.sql")
            ?? throw new InvalidOperationException("Embedded post-flight migration is missing.");
        using var reader = new StreamReader(stream);
        await using var command = source.CreateCommand(await reader.ReadToEndAsync());
        await command.ExecuteNonQueryAsync();
        using var lifecycleStream = typeof(PostFlightSchema).Assembly.GetManifestResourceStream(
            "DroneOps.Persistence.Migrations.007_post_flight_lifecycle.sql")
            ?? throw new InvalidOperationException("Embedded post-flight lifecycle migration is missing.");
        using var lifecycleReader = new StreamReader(lifecycleStream);
        await using var lifecycleCommand = source.CreateCommand(await lifecycleReader.ReadToEndAsync());
        await lifecycleCommand.ExecuteNonQueryAsync();
        using var snapshotStream = typeof(PostFlightSchema).Assembly.GetManifestResourceStream(
            "DroneOps.Persistence.Migrations.20261029000100_post_flight_execution_snapshot.sql")
            ?? throw new InvalidOperationException("Embedded post-flight execution snapshot migration is missing.");
        using var snapshotReader = new StreamReader(snapshotStream);
        await using var snapshotCommand = source.CreateCommand(await snapshotReader.ReadToEndAsync());
        await snapshotCommand.ExecuteNonQueryAsync();
        await CheckAsync(source);
    }

    public static async Task CheckAsync(NpgsqlDataSource source)
    {
        // Check every object required by migrations 006-007 without reading or
        // changing business data. Checking only the latest index/table is not
        // sufficient because a manually or partially provisioned database can
        // still be missing one of the lifecycle columns used by EF queries.
        await using var command = source.CreateCommand("""
            SELECT EXISTS (
                SELECT 1 FROM pg_indexes
                WHERE schemaname = current_schema()
                  AND tablename = 'PostFlightReport'
                  AND indexname = 'UX_PostFlightReport_Current'
            )
            AND EXISTS (
                SELECT 1 FROM information_schema.tables
                WHERE table_schema = current_schema()
                  AND table_name = 'FlightCompletionEvent'
            )
            AND EXISTS (
                SELECT 1 FROM pg_indexes
                WHERE schemaname = current_schema()
                  AND tablename = 'FlightCompletionEvent'
                  AND indexname = 'UX_FlightCompletionEvent_Mission_Trusted'
            )
            AND NOT EXISTS (
                SELECT 1
                FROM unnest(ARRAY[
                    'Revision',
                    'IsCurrent',
                    'GenerationStatus',
                    'CoveredTelemetryFrom',
                    'CoveredTelemetryThrough',
                    'GenerationStartedAt',
                    'GeneratedAt',
                    'FailedAt',
                    'FailureReason',
                    'RequestedBy',
                    'GenerationReason'
                ]) AS required(column_name)
                WHERE NOT EXISTS (
                    SELECT 1
                    FROM information_schema.columns AS actual
                    WHERE actual.table_schema = current_schema()
                      AND actual.table_name = 'PostFlightReport'
                      AND actual.column_name = required.column_name
                )
            )
            """);
        var exists = (bool)(await command.ExecuteScalarAsync() ?? false);
        if (!exists)
        {
            throw new InvalidOperationException(
                "Post-flight schema is out of date. Run the API with --migrate-postflight " +
                "or apply database/migrations/006_post_flight_integrity.sql followed by " +
                "database/migrations/007_post_flight_lifecycle.sql and " +
                "database/migrations/20261029000100_post_flight_execution_snapshot.sql.");
        }
    }
}
