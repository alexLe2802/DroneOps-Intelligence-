using Npgsql;

namespace DroneOps.Persistence;

public static class NotificationSchema
{
    public static async Task ApplyAsync(NpgsqlDataSource source)
    {
        using var stream = typeof(NotificationSchema).Assembly.GetManifestResourceStream(
            "DroneOps.Persistence.Migrations.009_notifications.sql")
            ?? throw new InvalidOperationException("Embedded notification migration is missing.");
        using var reader = new StreamReader(stream);
        await using var command = source.CreateCommand(await reader.ReadToEndAsync());
        await command.ExecuteNonQueryAsync();
    }
}
