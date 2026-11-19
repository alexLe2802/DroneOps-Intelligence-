using Npgsql;

namespace DroneOps.Persistence;

public static class PilotLicenseSchema
{
    private const string Resource =
        "DroneOps.Persistence.Migrations.20261119000100_pilot_license_lifecycle.sql";

    public static async Task ApplyAsync(
        NpgsqlDataSource source,
        CancellationToken cancellationToken = default)
    {
        await using var stream = typeof(PilotLicenseSchema).Assembly
            .GetManifestResourceStream(Resource)
            ?? throw new InvalidOperationException(
                $"Embedded Pilot License migration is missing: {Resource}.");
        using var reader = new StreamReader(stream);
        await using var command = source.CreateCommand(
            await reader.ReadToEndAsync(cancellationToken));
        command.CommandTimeout = 120;
        await command.ExecuteNonQueryAsync(cancellationToken);
        await CheckAsync(source, cancellationToken);
    }

    public static async Task CheckAsync(
        NpgsqlDataSource source,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await SchemaCommandExecutor.ExecuteAsync(source, """
                SELECT "LicenseStatus", "LicenseVersion"
                FROM public."Pilot" LIMIT 0;
                SELECT "Id", "PilotId", "Action", "PreviousStatus", "NewStatus", "Reason", "ActorId", "OccurredAt"
                FROM public."PilotLicenseAudit" LIMIT 0;
                """, cancellationToken);
        }
        catch (PostgresException exception) when (
            exception.SqlState is "42P01" or "42703")
        {
            throw new InvalidOperationException(
                "Apply the Pilot License migration with --migrate-pilot-licenses before starting the API.",
                exception);
        }
    }
}
