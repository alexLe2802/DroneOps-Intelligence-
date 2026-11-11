using DroneOps.Persistence;
using DroneOps.Persistence.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

// Compatibility fixture for Supabase PostgreSQL roles/Storage metadata. This does
// not claim to emulate hosted Auth, Storage APIs, billing, or a historical dump.
internal static class CiDatabaseChecks
{
    public static NpgsqlConnectionStringBuilder LocalConnection()
    {
        if (Environment.GetEnvironmentVariable("DRONEOPS_CI_ISOLATED") != "true")
            throw new InvalidOperationException("Disposable CI database isolation must be explicit.");
        var value = Environment.GetEnvironmentVariable("DRONEOPS_CI_POSTGRES")
            ?? throw new InvalidOperationException("DRONEOPS_CI_POSTGRES is required.");
        var connection = new NpgsqlConnectionStringBuilder(value);
        var host = connection.Host ?? "";
        if (host is not ("127.0.0.1" or "localhost" or "::1" or "postgres") && !host.StartsWith("/tmp/droneops-ci-", StringComparison.Ordinal))
            throw new InvalidOperationException("CI database checks reject hosted/shared database targets.");
        return connection;
    }

    public static async Task RunAsync(string root)
    {
        var connection = LocalConnection();
        connection.Database = Environment.GetEnvironmentVariable("DRONEOPS_CI_DATABASE") ?? "droneops_ci_app";
        if (!connection.Database.StartsWith("droneops_ci_", StringComparison.Ordinal) || connection.Database.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '_'))
            throw new InvalidOperationException("Use a disposable droneops_ci_* database.");
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connection.ConnectionString).Options);
        if (!await db.Database.EnsureCreatedAsync())
            throw new InvalidOperationException("CI migration fixture must be a new empty database; refusing to alter existing data.");
        await db.Database.ExecuteSqlRawAsync("""
            DO $$ BEGIN
              IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname='anon') THEN CREATE ROLE anon NOLOGIN; END IF;
              IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname='authenticated') THEN CREATE ROLE authenticated NOLOGIN; END IF;
              IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname='service_role') THEN CREATE ROLE service_role NOLOGIN; END IF;
            END $$;
            CREATE SCHEMA IF NOT EXISTS storage;
            CREATE TABLE storage.buckets (id text PRIMARY KEY, name text NOT NULL, public boolean NOT NULL DEFAULT false,
                file_size_limit bigint, allowed_mime_types text[]);
            """);
        var directory = Path.Combine(root, "database", "migrations");
        var names = File.ReadAllLines(Path.Combine(root, "database", "migration-order.txt"))
            .Select(s => s.Trim()).Where(s => s.Length > 0 && !s.StartsWith('#')).ToArray();
        if (names.Distinct().Count() != names.Length || names.Any(n => Path.GetFileName(n) != n))
            throw new InvalidOperationException("Invalid or duplicate migration-order entry.");
        var unlisted = Directory.GetFiles(directory, "*.sql").Select(Path.GetFileName).Except(names).ToArray();
        if (unlisted.Length > 0) throw new InvalidOperationException("Migrations missing from migration-order: " + string.Join(", ", unlisted));
        // Bootstrap is the current EF model, because submitted migrations assume
        // an existing base application schema. Do not describe this as replaying
        // the complete historical schema from zero.
        await using var source = NpgsqlDataSource.Create(connection.ConnectionString);
        // Migrations are applied once in order, like Supabase's migration journal.
        // Re-running 001 after 003 would reintroduce a legacy role intentionally
        // forbidden by 003; that is not how a history-aware deployment works.
        foreach (var name in names)
        {
            try
            {
                await using var command = source.CreateCommand(await File.ReadAllTextAsync(Path.Combine(directory, name)));
                command.CommandTimeout = 30;
                await command.ExecuteNonQueryAsync();
            }
            catch (Exception ex) { throw new InvalidOperationException($"Migration compatibility failed: {name}.", ex); }
        }
        Console.WriteLine($"PASS: ordered migration compatibility ({names.Length} files), current EF base schema");
        // The newly added release migration promises IF NOT EXISTS safety.
        // Verify that promise on the fully upgraded fixture, not obsolete SQL.
        await using (var repeat = source.CreateCommand(await File.ReadAllTextAsync(Path.Combine(directory, names[^1]))))
            await repeat.ExecuteNonQueryAsync();
        Console.WriteLine($"PASS: latest release migration is safe to repeat: {names[^1]}");
        await MissionWorkflowSchema.CheckAsync(source);
        await TelemetrySchema.CheckAsync(source);
        await PostFlightSchema.CheckAsync(source);
        await AccountWorkflowSchema.CheckAsync(source);
        await DroneConnectionSchema.CheckAsync(source);
        await FlightPreparationSchema.CheckAsync(source);
        await UavImageSchema.CheckAsync(source);
        await using var check = source.CreateCommand("""
            SELECT count(*) FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
            WHERE n.nspname='public' AND c.relname IN ('MissionUpdateRequest','MissionValidationRun','MissionFlightExecution')
              AND c.relrowsecurity;
            """);
        if (Convert.ToInt64(await check.ExecuteScalarAsync()) != 3)
            throw new InvalidOperationException("Required operational tables must retain RLS.");
        Console.WriteLine("PASS: Supabase-compatible schema startup checks and operational RLS. No hosted database was accessed.");
    }
}
