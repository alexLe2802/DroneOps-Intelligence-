using Npgsql;
namespace DroneOps.Persistence;
public static class DroneConnectionSchema
{
    public static async Task ApplyAsync(NpgsqlDataSource source)
    {
        foreach(var resource in new[]{"DroneOps.Persistence.DroneConnections.sql","DroneOps.Persistence.DroneHardware.sql"})
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
        await using var command=source.CreateCommand("SELECT \"TokenHash\", \"LastSampleAt\" FROM public.\"DroneConnection\" LIMIT 0");
        try
        {
            await command.ExecuteNonQueryAsync();
            await using var binding=source.CreateCommand("SELECT h.\"Identity\", c.\"HardwareIdentity\" FROM public.\"DroneHardwareIdentity\" h CROSS JOIN public.\"DroneConnection\" c LIMIT 0");
            await binding.ExecuteNonQueryAsync();
        }
        catch(PostgresException e) when(e.SqlState is "42P01" or "42703")
        { throw new InvalidOperationException("Run --migrate-drone-connections before starting this API version.",e); }
    }
}
