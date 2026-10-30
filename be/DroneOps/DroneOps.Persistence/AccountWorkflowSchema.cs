using Npgsql;

namespace DroneOps.Persistence;

public static class AccountWorkflowSchema
{
    private const string Resource="DroneOps.Persistence.AccountWorkflows.sql";
    public static async Task ApplyAsync(NpgsqlDataSource source)
    {
        using var stream=typeof(AccountWorkflowSchema).Assembly.GetManifestResourceStream(Resource)
            ??throw new InvalidOperationException("Account workflow migration resource missing.");
        using var reader=new StreamReader(stream);
        await using var command=source.CreateCommand(await reader.ReadToEndAsync());
        await command.ExecuteNonQueryAsync();await CheckAsync(source);
    }
    public static async Task CheckAsync(NpgsqlDataSource source)
    {
        try
        {
            await using var command=source.CreateCommand("""
                SELECT u."ProfileVersion",c."CodeHash",a."Result",d."TargetUserId"
                FROM public."User" u
                LEFT JOIN droneops."EmailChangeChallenge" c ON false
                LEFT JOIN droneops."AccountWorkflowAudit" a ON false
                LEFT JOIN public."PilotUpdateDecisionAudit" d ON false LIMIT 0
                """);
            await command.ExecuteNonQueryAsync();
        }
        catch(PostgresException e) when(e.SqlState is "42P01" or "42703")
        {throw new InvalidOperationException("Apply account workflow migration with --migrate-account-workflows before starting the API.",e);}
    }
}
