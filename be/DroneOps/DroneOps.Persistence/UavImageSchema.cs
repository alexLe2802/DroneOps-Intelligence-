using Npgsql;

namespace DroneOps.Persistence;

public static class UavImageSchema
{
    public static async Task ApplyAsync(NpgsqlDataSource source)
    {
        foreach (var resource in new[]
        {
            "DroneOps.Persistence.Migrations.20261026000100_uav_images.sql",
            "DroneOps.Persistence.Migrations.20261105000100_uav_image_moderation_queue.sql"
        })
        {
            using var stream = typeof(UavImageSchema).Assembly.GetManifestResourceStream(resource)
                ?? throw new InvalidOperationException($"Embedded UAV image migration is missing: {resource}.");
            using var reader = new StreamReader(stream);
            await using var command = source.CreateCommand(await reader.ReadToEndAsync());
            command.CommandTimeout = 120;
            await command.ExecuteNonQueryAsync();
        }

        await CheckAsync(source);
    }

    public static async Task CheckAsync(NpgsqlDataSource source)
    {
        try
        {
            await using var command = source.CreateCommand("""
                SELECT "ImageStoragePath", "ImageModerationStatus", "ImageModerationAttempts",
                       "ImageModerationNextAttemptAt", "ImageModeratedAt", "ImageModerationReason"
                FROM public."UAV" LIMIT 0
                """);
            await command.ExecuteNonQueryAsync();
        }
        catch (PostgresException exception) when (exception.SqlState is "42P01" or "42703")
        {
            throw new InvalidOperationException(
                "Apply the UAV image migration with --migrate-uav-images before starting the API.", exception);
        }
    }
}
