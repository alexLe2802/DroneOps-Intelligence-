using Npgsql;

namespace DroneOps.Persistence.Auth;

public sealed class PostgresAuthStore(NpgsqlDataSource dataSource) : IAuthStore
{
    private static Account ReadAccount(NpgsqlDataReader r) => new(r.GetGuid(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetBoolean(4));

    public async Task<SessionIdentity?> CreateSessionAsync(string uid, string email, string tokenHash, string loginProofHash,
        DateTimeOffset expiresAt, string userAgent, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);
        // Lock the provisioned account to serialize binding, session creation, and account deactivation.
        await using var lookup = new NpgsqlCommand("""
            SELECT id, email, display_name, role_code, is_active, firebase_uid
            FROM droneops.accounts WHERE email = @email FOR UPDATE
            """, connection, tx);
        lookup.Parameters.AddWithValue("email", email.ToLowerInvariant());
        Account account;
        await using (var reader = await lookup.ExecuteReaderAsync(ct))
        {
            if (!await reader.ReadAsync(ct) || !reader.GetBoolean(4) ||
                (!reader.IsDBNull(5) && reader.GetString(5) != uid)) return null;
            account = ReadAccount(reader);
        }
        var sessionId = Guid.NewGuid();
        await using var insert = new NpgsqlCommand("""
            UPDATE droneops.accounts SET firebase_uid = @uid, updated_at = now() WHERE id = @account;
            INSERT INTO droneops.auth_sessions(id, account_id, token_hash, login_proof_hash, expires_at, user_agent)
            VALUES (@id, @account, @hash, @proof, @expires, @agent);
            INSERT INTO droneops.auth_audit(actor_id, target_id, action) VALUES (@account, @account, 'session.created');
            """, connection, tx);
        insert.Parameters.AddWithValue("uid", uid);
        insert.Parameters.AddWithValue("account", account.Id);
        insert.Parameters.AddWithValue("id", sessionId);
        insert.Parameters.AddWithValue("hash", tokenHash);
        insert.Parameters.AddWithValue("proof", loginProofHash);
        insert.Parameters.AddWithValue("expires", expiresAt);
        insert.Parameters.AddWithValue("agent", userAgent.Length > 300 ? userAgent[..300] : userAgent);
        try { await insert.ExecuteNonQueryAsync(ct); }
        catch (PostgresException e) when (e.SqlState == PostgresErrorCodes.UniqueViolation) { return null; }
        await tx.CommitAsync(ct);
        return new(account, sessionId, expiresAt);
    }

    public async Task<SessionIdentity?> FindSessionAsync(string uid, string tokenHash, CancellationToken ct)
    {
        await using var cmd = dataSource.CreateCommand("""
            SELECT a.id, a.email, a.display_name, a.role_code, a.is_active, s.id, s.expires_at
            FROM droneops.accounts a JOIN droneops.auth_sessions s ON s.account_id = a.id
            WHERE a.firebase_uid = @uid AND a.is_active AND s.token_hash = @hash
            AND s.revoked_at IS NULL AND s.expires_at > now()
            """);
        cmd.Parameters.AddWithValue("uid", uid);
        cmd.Parameters.AddWithValue("hash", tokenHash);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct) ? new(ReadAccount(r), r.GetGuid(5), r.GetFieldValue<DateTimeOffset>(6)) : null;
    }

    public async Task<IReadOnlyList<SessionInfo>> ListSessionsAsync(Guid accountId, CancellationToken ct)
    {
        await using var cmd = dataSource.CreateCommand("""
            SELECT id, created_at, expires_at, user_agent FROM droneops.auth_sessions
            WHERE account_id = @account AND revoked_at IS NULL AND expires_at > now() ORDER BY created_at DESC
            """);
        cmd.Parameters.AddWithValue("account", accountId);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        var result = new List<SessionInfo>();
        while (await r.ReadAsync(ct)) result.Add(new(r.GetGuid(0), r.GetFieldValue<DateTimeOffset>(1), r.GetFieldValue<DateTimeOffset>(2), r.GetString(3)));
        return result;
    }

    public Task RevokeSessionAsync(Guid accountId, Guid sessionId, CancellationToken ct) => Revoke(accountId, sessionId, ct);
    public Task RevokeAllAsync(Guid accountId, CancellationToken ct) => Revoke(accountId, null, ct);
    private async Task Revoke(Guid accountId, Guid? sessionId, CancellationToken ct)
    {
        await using var cmd = dataSource.CreateCommand("""
            WITH revoked AS (
                UPDATE droneops.auth_sessions SET revoked_at = now()
                WHERE account_id = @account AND revoked_at IS NULL AND (@id IS NULL OR id = @id) RETURNING account_id
            ) INSERT INTO droneops.auth_audit(actor_id, target_id, action)
            SELECT account_id, account_id, 'session.revoked' FROM revoked
            """);
        cmd.Parameters.AddWithValue("account", accountId);
        cmd.Parameters.AddWithValue("id", NpgsqlTypes.NpgsqlDbType.Uuid, (object?)sessionId ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<Account>> ListAccountsAsync(CancellationToken ct)
    {
        await using var cmd = dataSource.CreateCommand("SELECT id, email, display_name, role_code, is_active FROM droneops.accounts ORDER BY created_at DESC");
        await using var r = await cmd.ExecuteReaderAsync(ct);
        var result = new List<Account>();
        while (await r.ReadAsync(ct)) result.Add(ReadAccount(r));
        return result;
    }

    public async Task<bool> ProvisionOperatorAsync(string email, string displayName, Guid managerId, CancellationToken ct)
    {
        await using var cmd = dataSource.CreateCommand("""
            WITH added AS (
                INSERT INTO droneops.accounts(id, email, display_name, role_code, created_by)
                VALUES (@id, @email, @name, 'uav_operator', @manager) ON CONFLICT (email) DO NOTHING RETURNING id
            ) INSERT INTO droneops.auth_audit(actor_id, target_id, action)
            SELECT @manager, id, 'operator.provisioned' FROM added
            """);
        cmd.Parameters.AddWithValue("id", Guid.NewGuid());
        cmd.Parameters.AddWithValue("email", email.Trim().ToLowerInvariant());
        cmd.Parameters.AddWithValue("name", displayName.Trim());
        cmd.Parameters.AddWithValue("manager", managerId);
        return await cmd.ExecuteNonQueryAsync(ct) == 1;
    }

    public async Task<bool> SetOperatorAccessAsync(Guid accountId, bool active, Guid managerId, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);
        // Managers cannot disable/demote another manager (including the last active manager).
        await using var cmd = new NpgsqlCommand("""
            UPDATE droneops.accounts SET is_active = @active, updated_at = now()
            WHERE id = @id AND role_code = 'uav_operator'
            """, connection, tx);
        cmd.Parameters.AddWithValue("id", accountId);
        cmd.Parameters.AddWithValue("active", active);
        if (await cmd.ExecuteNonQueryAsync(ct) != 1) return false;
        await using var audit = new NpgsqlCommand("""
            UPDATE droneops.auth_sessions SET revoked_at = now() WHERE account_id = @id AND NOT @active AND revoked_at IS NULL;
            INSERT INTO droneops.auth_audit(actor_id, target_id, action) VALUES (@manager, @id, @action);
            """, connection, tx);
        audit.Parameters.AddWithValue("id", accountId);
        audit.Parameters.AddWithValue("active", active);
        audit.Parameters.AddWithValue("manager", managerId);
        audit.Parameters.AddWithValue("action", active ? "operator.enabled" : "operator.disabled");
        await audit.ExecuteNonQueryAsync(ct);
        await tx.CommitAsync(ct);
        return true;
    }
}
