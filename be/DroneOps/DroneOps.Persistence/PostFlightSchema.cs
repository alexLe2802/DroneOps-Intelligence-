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
        await CheckAsync(source);
    }

    public static async Task CheckAsync(NpgsqlDataSource source)
    {
        // Kiểm tra unique index bằng catalog, không đọc hoặc thay đổi dữ liệu nghiệp vụ.
        await using var command = source.CreateCommand("""
            SELECT EXISTS (
                SELECT 1 FROM pg_indexes
                WHERE schemaname = current_schema()
                  AND tablename = 'PostFlightReport'
                  AND indexname = 'UX_PostFlightReport_MissionId'
            )
            """);
        var exists = (bool)(await command.ExecuteScalarAsync() ?? false);
        if (!exists)
        {
            throw new InvalidOperationException(
                "Post-flight schema is out of date. Run the API with --migrate-postflight " +
                "or apply database/migrations/006_post_flight_integrity.sql.");
        }
    }
}
