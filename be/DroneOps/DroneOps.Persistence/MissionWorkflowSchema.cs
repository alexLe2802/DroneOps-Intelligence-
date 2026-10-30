using Npgsql;
namespace DroneOps.Persistence;

public static class MissionWorkflowSchema
{
    public static async Task ApplyAsync(NpgsqlDataSource source)
    {
        foreach (var resource in new[] {"DroneOps.Persistence.MissionWorkflow.sql","DroneOps.Persistence.MissionRequests.sql","DroneOps.Persistence.MissionRestrictions.sql"})
        {
            using var stream=typeof(MissionWorkflowSchema).Assembly.GetManifestResourceStream(resource)??throw new InvalidOperationException("Workflow migration resource missing.");
            using var reader=new StreamReader(stream);
            await using var command=source.CreateCommand(await reader.ReadToEndAsync());
            await command.ExecuteNonQueryAsync();
        }
        await CheckAsync(source);
    }
    public static async Task CheckAsync(NpgsqlDataSource source)
    {
        await using var command = source.CreateCommand("SELECT \"Id\", \"ResultJson\", \"Stage\" FROM public.\"MissionValidationRun\" LIMIT 0");
        try
        {
            await command.ExecuteNonQueryAsync();
            await using var audit = source.CreateCommand("SELECT \"ValidationRunId\" FROM public.\"MissionApproval\" LIMIT 0");
            await audit.ExecuteNonQueryAsync();
            await using var requestCheck=source.CreateCommand("SELECT \"Id\", \"ProposedPlanJson\" FROM public.\"MissionUpdateRequest\" LIMIT 0");
            await requestCheck.ExecuteNonQueryAsync();
            foreach (var sql in new[] {
                "SELECT \"BasePlanJson\", \"ValidationRunId\" FROM public.\"MissionUpdateRequest\" LIMIT 0",
                "SELECT \"UpdateRequestId\" FROM public.\"MissionValidationRun\" LIMIT 0",
                "SELECT \"ResultStatus\", \"RestrictionRevision\", \"EvidenceJson\" FROM public.\"MissionValidationRun\" LIMIT 0",
                "SELECT \"UpdateRequestId\" FROM public.\"MissionApproval\" LIMIT 0",
                "SELECT \"AltitudeReference\" FROM public.\"Mission\" LIMIT 0",
                "SELECT \"RestrictionType\", \"VerticalRule\", \"Revision\", \"AltitudeReference\", \"MinAltitudeMeters\", \"MaxAltitudeMeters\" FROM public.\"Geofence\" LIMIT 0",
                "SELECT \"SnapshotJson\" FROM public.\"GeofenceRevision\" LIMIT 0",
                "SELECT \"ForecastJson\" FROM public.\"MissionWeatherCache\" LIMIT 0"
            })
            {
                await using var check = source.CreateCommand(sql);
                await check.ExecuteNonQueryAsync();
            }
        }
        catch (PostgresException e) when (e.SqlState is "42P01" or "42703") { throw new InvalidOperationException("Apply mission workflow migration with --migrate-mission-workflow before starting the API.", e); }
    }
}
