using Npgsql;
namespace DroneOps.Persistence;

public static class MissionWorkflowSchema
{
    public static async Task ApplyAsync(NpgsqlDataSource source)
    {
        foreach (var resource in new[] {"DroneOps.Persistence.MissionWorkflow.sql","DroneOps.Persistence.MissionRequests.sql","DroneOps.Persistence.MissionRestrictions.sql","DroneOps.Persistence.MissionReviewIntegrity.sql","DroneOps.Persistence.RegistryDocuments.sql"})
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
        try
        {
            // One command keeps startup verification to a single network round-trip.
            await SchemaCommandExecutor.ExecuteAsync(source, """
                SELECT "Id", "ResultJson", "Stage", "UpdateRequestId", "ResultStatus",
                       "RestrictionRevision", "EvidenceJson"
                FROM public."MissionValidationRun" LIMIT 0;
                SELECT "Id", "MissionId", "UavId", "UploadedBy", "UploadedAt", "Name", "Content", "Decision", "ReviewedBy", "ReviewedAt", "Reason" FROM public."RegistryDocument" LIMIT 0;
                SELECT "ValidationRunId", "UpdateRequestId", "PermitDecision", "PermitDocumentId"
                FROM public."MissionApproval" LIMIT 0;
                SELECT "Id", "ProposedPlanJson", "PilotConfirmedBy", "PilotConfirmedAt",
                       "PilotConfirmedValidationRunId", "BasePlanJson", "ValidationRunId"
                FROM public."MissionUpdateRequest" LIMIT 0;
                SELECT "AltitudeReference" FROM public."Mission" LIMIT 0;
                SELECT "RestrictionType", "VerticalRule", "Revision", "AltitudeReference",
                       "MinAltitudeMeters", "MaxAltitudeMeters"
                FROM public."Geofence" LIMIT 0;
                SELECT "SnapshotJson" FROM public."GeofenceRevision" LIMIT 0;
                SELECT "ForecastJson" FROM public."MissionWeatherCache" LIMIT 0;
                SELECT "Id", "CreatedBy", "UAVId", "Name", "Description", "Status",
                       "AltitudeReference", "AutomaticFlightRequested", "AutomaticFlightLimitsJson",
                       "StartTime", "EndTime", "AverageSpeedMetersPerSecond",
                       "MaxSpeedMetersPerSecond", "CreatedAt"
                FROM public."Mission" LIMIT 0;
                SELECT "Id", "MissionId", "SequenceOrder", "Latitude", "Longitude",
                       "Altitude", "NavigationJson", "ActionType", "CreatedAt"
                FROM public."Waypoint" LIMIT 0;
                SELECT "Id", "PilotId", "Status", "ManufacturerDocumentId" FROM public."UAV" LIMIT 0;
                SELECT "Id", "UserId", "PilotLicenseNo", "LicenseIssuedDate",
                       "LicenseExpiredDate"
                FROM public."Pilot" LIMIT 0;
                SELECT "Id", "IsActive" FROM public."User" LIMIT 0;
                """);
        }
        catch (PostgresException e) when (e.SqlState is "42P01" or "42703") { throw new InvalidOperationException("Apply mission workflow migration with --migrate-mission-workflow before starting the API.", e); }
    }
}
