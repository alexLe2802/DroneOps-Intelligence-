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
                command.CommandTimeout = 30;
                return await execute(command, cancellationToken);
            }
            catch (NpgsqlException exception) when (
                exception is not PostgresException &&
                exception.IsTransient &&
                attempt < MaxAttempts)
            {
                // A broken pooled connection is discarded by Npgsql. Give a remote
                // pooler enough time to recover while keeping startup bounded.
                await Task.Delay(TimeSpan.FromMilliseconds(500 * attempt * attempt), cancellationToken);
            }
            catch (NpgsqlException exception) when (exception is not PostgresException)
            {
                var cause = exception.GetBaseException();
                throw new InvalidOperationException(
                    $"PostgreSQL schema verification could not complete after {attempt} attempt(s). " +
                    $"{cause.GetType().Name}: {cause.Message} " +
                    "Check the configured host/port, Supabase pooler health and network, then try again.",
                    exception);
            }
        }
    }
}
