using Npgsql;
namespace DroneOps.Persistence;
public static class DroneConnectionSchema
{
    public static async Task ApplyAsync(NpgsqlDataSource source)
    {
        foreach(var resource in new[]{"DroneOps.Persistence.DroneConnections.sql","DroneOps.Persistence.DroneHardware.sql","DroneOps.Persistence.OptionalConnectionMission.sql"})
        {
            using var stream=typeof(DroneConnectionSchema).Assembly.GetManifestResourceStream(resource)!;
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
            await using var column = source.CreateCommand("""
                SELECT is_nullable FROM information_schema.columns
                WHERE table_schema = 'public' AND table_name = 'DroneConnection' AND column_name = 'MissionId'
                """);
            if (!Equals(await column.ExecuteScalarAsync(), "YES"))
                throw new InvalidOperationException("Apply the optional connection mission migration before starting this API version.");
            await SchemaCommandExecutor.ExecuteAsync(source, """
                SELECT "TokenHash", "LastSampleAt" FROM public."DroneConnection" LIMIT 0;
                SELECT h."Identity", c."HardwareIdentity"
                FROM public."DroneHardwareIdentity" h
                CROSS JOIN public."DroneConnection" c LIMIT 0;
                """);
        }
        catch(PostgresException e) when(e.SqlState is "42P01" or "42703")
        { throw new InvalidOperationException("Run --migrate-drone-connections before starting this API version.",e); }
    }
}
