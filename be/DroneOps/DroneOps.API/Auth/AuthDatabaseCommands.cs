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
            var sql = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Migrations", "001_auth.sql"));
            await using var migration = db.CreateCommand(sql);
            await migration.ExecuteNonQueryAsync();
            Console.WriteLine("Auth schema migration completed.");
        }
        if (args.Contains("--bootstrap-manager"))
        {
            var email = config["Auth:BootstrapManagerEmail"]?.Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(email) || !System.Net.Mail.MailAddress.TryCreate(email, out _))
                throw new InvalidOperationException("Configure a valid Auth:BootstrapManagerEmail first.");
            await using var connection = await db.OpenConnectionAsync();
            await using var tx = await connection.BeginTransactionAsync();
            await using var cmd = new NpgsqlCommand("""
                LOCK TABLE droneops.accounts IN EXCLUSIVE MODE;
                WITH added AS (
                    INSERT INTO droneops.accounts(id, email, display_name, role_code)
                    SELECT @id, @email, 'Operations Manager', 'operations_manager'
                    WHERE NOT EXISTS (SELECT 1 FROM droneops.accounts WHERE role_code = 'operations_manager')
                    ON CONFLICT (email) DO NOTHING RETURNING id
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
