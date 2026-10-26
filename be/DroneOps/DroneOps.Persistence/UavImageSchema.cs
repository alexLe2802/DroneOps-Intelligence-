using Npgsql;

namespace DroneOps.Persistence;

public static class UavImageSchema
{
    public static async Task ApplyAsync(NpgsqlDataSource source)
    {
        using var stream = typeof(UavImageSchema).Assembly.GetManifestResourceStream(
            "DroneOps.Persistence.Migrations.20261026000100_uav_images.sql")
            ?? throw new InvalidOperationException("Embedded UAV image migration is missing.");
        using var reader = new StreamReader(stream);
        await using var command = source.CreateCommand(await reader.ReadToEndAsync());
        await command.ExecuteNonQueryAsync();
    }
}
