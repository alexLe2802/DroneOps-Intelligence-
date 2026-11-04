using Npgsql;

namespace DroneOps.Persistence;

public static class FlightPreparationSchema
{
    public static async Task CheckAsync(NpgsqlDataSource source, CancellationToken ct = default)
    {
        await using var command = source.CreateCommand("""
            SELECT "AutomaticFlightRequested" FROM public."Mission" LIMIT 0;
            SELECT "TokenHash", "SystemId", "ComponentId" FROM public."GatewayEnrollment" LIMIT 0;
            SELECT "ConfirmedPlanHash", "ConnectionId", "LastPresenceAt", "ExecutionJson" FROM public."MissionAutomationState" LIMIT 0;
            """);
        try { await command.ExecuteNonQueryAsync(ct); }
        catch (PostgresException error) when (error.SqlState is "42P01" or "42703")
        {
            throw new InvalidOperationException("Apply the gateway preparation and manual_arm_countdown migrations before deploying this API version.", error);
        }
    }
}
