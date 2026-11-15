using Npgsql;

namespace DroneOps.Persistence;

public static class AccountWorkflowSchema
{
    private static readonly string[] Resources =
    [
        "DroneOps.Persistence.AccountWorkflows.sql",
        "DroneOps.Persistence.PilotFlightExperience.sql"
    ];

    public static async Task ApplyAsync(NpgsqlDataSource source)
    {
        foreach (var resource in Resources)
        {
            using var stream = typeof(AccountWorkflowSchema).Assembly.GetManifestResourceStream(resource)
                ?? throw new InvalidOperationException($"Account workflow migration resource is missing: {resource}.");
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
            await SchemaCommandExecutor.ExecuteAsync(source, """
                SELECT u."ProfileVersion",c."CodeHash",a."Result",d."TargetUserId"
                FROM public."User" u
                LEFT JOIN droneops."EmailChangeChallenge" c ON false
                LEFT JOIN droneops."AccountWorkflowAudit" a ON false
                LEFT JOIN public."PilotUpdateDecisionAudit" d ON false LIMIT 0;
                SELECT "FlightExperienceDescription"
                FROM public."Pilot" LIMIT 0;
                """);
        }
        catch (PostgresException e) when (e.SqlState is "42P01" or "42703")
        {
            throw new InvalidOperationException(
                "Apply account workflow migration with --migrate-account-workflows before starting the API.",
                e);
        }
    }
}
