using Npgsql;

namespace DroneOps.Persistence;

internal static class SchemaCommandExecutor
{
    private const int MaxAttempts = 3;

    public static Task ExecuteAsync(
        NpgsqlDataSource source,
        string sql,
        CancellationToken cancellationToken = default) =>
        ExecuteWithRetryAsync(
            source,
            sql,
            static async (command, ct) =>
            {
                await command.ExecuteNonQueryAsync(ct);
                return true;
            },
            cancellationToken);

    public static Task<object?> ExecuteScalarAsync(
        NpgsqlDataSource source,
        string sql,
        CancellationToken cancellationToken = default) =>
        ExecuteWithRetryAsync(
            source,
            sql,
            static (command, ct) => command.ExecuteScalarAsync(ct),
            cancellationToken);

    private static async Task<T> ExecuteWithRetryAsync<T>(
        NpgsqlDataSource source,
        string sql,
        Func<NpgsqlCommand, CancellationToken, Task<T>> execute,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await using var command = source.CreateCommand(sql);
                command.CommandTimeout = 15;
                return await execute(command, cancellationToken);
            }
            catch (NpgsqlException exception) when (
                exception is not PostgresException &&
                exception.IsTransient &&
                attempt < MaxAttempts)
            {
                // A broken pooled connection is discarded by Npgsql. A short delay
                // lets a remote pooler recover without hiding schema errors.
                await Task.Delay(TimeSpan.FromMilliseconds(150 * attempt), cancellationToken);
            }
            catch (NpgsqlException exception) when (exception is not PostgresException)
            {
                throw new InvalidOperationException(
                    $"PostgreSQL schema verification could not complete after {attempt} attempt(s). " +
                    "Check the database/pooler connection and try again.",
                    exception);
            }
        }
    }
}
