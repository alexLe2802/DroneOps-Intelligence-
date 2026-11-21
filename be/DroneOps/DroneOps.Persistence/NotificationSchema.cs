using Npgsql;

namespace DroneOps.Persistence;

public static class NotificationSchema
{
    public static async Task ApplyAsync(
        NpgsqlDataSource source,
        CancellationToken cancellationToken = default)
    {
        using var stream = typeof(NotificationSchema).Assembly.GetManifestResourceStream(
            "DroneOps.Persistence.Migrations.009_notifications.sql")
            ?? throw new InvalidOperationException("Embedded notification migration is missing.");
        using var reader = new StreamReader(stream);
        var sql = await reader.ReadToEndAsync(cancellationToken);

        // The migration contains only idempotent CREATE ... IF NOT EXISTS
        // statements, so retrying the entire batch after a transient pooler
        // disconnect is safe and prevents one broken connection from aborting API
        // startup.
        await SchemaCommandExecutor.ExecuteAsync(source, sql, cancellationToken);
    }

    public static async Task CheckAsync(
        NpgsqlDataSource source,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await SchemaCommandExecutor.ExecuteAsync(source, """
                SELECT "Id", "RecipientUserId", "ActorUserId", "Type", "Title", "Message",
                       "ReferenceId", "ReferenceType", "ActionUrl", "IsRead", "ReadAt", "CreatedAt"
                FROM public."Notification" LIMIT 0
                """, cancellationToken);
        }
        catch (PostgresException exception) when (exception.SqlState is "42P01" or "42703")
        {
            throw new InvalidOperationException(
                "Apply the notification migration with --migrate-notifications before starting the API.",
                exception);
        }
    }
}
