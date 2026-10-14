using Npgsql;

namespace DroneOps.API.Auth;

public static class AuthDatabaseCommands
{
    public static async Task<bool> RunAsync(string[] args, IServiceProvider services, IConfiguration config)
    {
        if (!args.Contains("--migrate-auth") && !args.Contains("--bootstrap-manager")) return false;
        var db = services.GetRequiredService<NpgsqlDataSource>();
        if (args.Contains("--migrate-auth"))
        {
            foreach (var path in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Migrations"), "*.sql").Order())
            {
                var sql = await File.ReadAllTextAsync(path);
                await using var migration = db.CreateCommand(sql);
                await migration.ExecuteNonQueryAsync();
                Console.WriteLine($"Migration {Path.GetFileName(path)} completed.");
            }
        }
        if (args.Contains("--bootstrap-manager"))
        {
            var email = config["Auth:BootstrapManagerEmail"]?.Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(email) || !System.Net.Mail.MailAddress.TryCreate(email, out _))
                throw new InvalidOperationException("Configure a valid Auth:BootstrapManagerEmail first.");
            await using var connection = await db.OpenConnectionAsync();
            await using var tx = await connection.BeginTransactionAsync();
            await using var cmd = new NpgsqlCommand("""
                LOCK TABLE public."User" IN EXCLUSIVE MODE;
                WITH added AS (
                    INSERT INTO public."User"("Id", "Email", "FullName", "RoleId", "PasswordHash", "CreatedAt", "WebAccessProvisioned")
                    SELECT @id, @email, 'Operations Manager', r."Id", '', now(), true
                    FROM public."Role" r WHERE r."Name" = 'operation_manager'
                    AND NOT EXISTS (SELECT 1 FROM public."User" u JOIN public."Role" mr ON mr."Id" = u."RoleId"
                                    WHERE mr."Name" = 'operation_manager' AND u."WebAccessProvisioned")
                    ON CONFLICT DO NOTHING RETURNING "Id" AS id
                ) INSERT INTO droneops.auth_audit(target_id, action) SELECT id, 'manager.bootstrapped' FROM added
                """, connection, tx);
            cmd.Parameters.AddWithValue("id", Guid.NewGuid());
            cmd.Parameters.AddWithValue("email", email);
            var added = await cmd.ExecuteNonQueryAsync();
            await tx.CommitAsync();
            Console.WriteLine(added > 0 ? "Initial Operations Manager provisioned." : "No account changed: a manager or matching account already exists.");
        }
        return true;
    }
}
