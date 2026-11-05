using System.Security.Cryptography;
using DroneOps.Application.DTOs.Response.Telemetry;
using DroneOps.Application.Services.Telemetry;
using Npgsql;

namespace DroneOps.API;

// A trusted device selects only a mission connection explicitly created by its owner.
// It never creates a mission, changes the hardware binding, or starts a flight.
public sealed class TrustedGatewayStore(NpgsqlDataSource source)
{
    public async Task<HashSet<Guid>> RegisteredUavsAsync(Guid owner, CancellationToken ct)
    {
        await using var command = source.CreateCommand("SELECT \"UavId\" FROM public.\"GatewayEnrollment\" WHERE \"OwnerId\" = @owner AND \"RevokedAt\" IS NULL");
        command.Parameters.AddWithValue("owner", owner);
        var ids = new HashSet<Guid>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) ids.Add(reader.GetGuid(0));
        return ids;
    }

    public async Task<string> EnrollAsync(GatewayCredential credential, CancellationToken ct)
    {
        var secret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        await using var command = source.CreateCommand("""
            INSERT INTO public."GatewayEnrollment" ("UavId", "OwnerId", "HardwareIdentity", "TokenHash", "GatewayName", "SystemId", "ComponentId")
            SELECT "UavId", "OwnerId", "HardwareIdentity", @hash, "GatewayName", "SystemId", "ComponentId" FROM public."DroneConnection"
            WHERE "Id" = @id AND "RevokedAt" IS NULL
            ON CONFLICT ("UavId") DO UPDATE SET "OwnerId" = EXCLUDED."OwnerId",
                "HardwareIdentity" = EXCLUDED."HardwareIdentity", "TokenHash" = EXCLUDED."TokenHash",
                "RevokedAt" = NULL, "EnrolledAt" = now(), "GatewayName" = EXCLUDED."GatewayName",
                "SystemId" = EXCLUDED."SystemId", "ComponentId" = EXCLUDED."ComponentId"
            """);
        command.Parameters.AddWithValue("hash", DroneConnectionService.Hash(secret));
        command.Parameters.AddWithValue("id", credential.ConnectionId);
        if (await command.ExecuteNonQueryAsync(ct) != 1) throw new InvalidOperationException("Connection was revoked during enrollment. Pair again.");
        return secret;
    }

    public async Task<GatewayCredential?> ResumeAsync(Guid uavId, string token, string identity, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length != 64 || !token.All(Uri.IsHexDigit)) throw new UnauthorizedAccessException("Invalid trusted gateway credential.");
        await using var connection = await source.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        // Lock enrollment against revocation and replacement while issuing a short mission lease.
        await using var device = new NpgsqlCommand("""
            SELECT e."OwnerId", e."GatewayName", e."SystemId", e."ComponentId" FROM public."GatewayEnrollment" e
            JOIN public."DroneHardwareIdentity" h ON h."UavId" = e."UavId" AND h."Identity" = e."HardwareIdentity"
            JOIN public."UAV" u ON u."Id" = e."UavId" AND u."Status" = 'Active'
            JOIN public."Pilot" p ON p."Id" = u."PilotId" AND p."UserId" = e."OwnerId"
            JOIN public."User" owner ON owner."Id" = e."OwnerId" AND owner."IsActive"
            WHERE e."UavId" = @uav AND e."TokenHash" = @hash AND e."HardwareIdentity" = @identity
                AND e."RevokedAt" IS NULL FOR UPDATE OF e
            """, connection, transaction);
        device.Parameters.AddWithValue("uav", uavId);
        device.Parameters.AddWithValue("hash", DroneConnectionService.Hash(token));
        device.Parameters.AddWithValue("identity", identity);
        Guid ownerId;
        string gatewayName;
        int systemId, componentId;
        await using (var reader = await device.ExecuteReaderAsync(ct))
        {
            if (!await reader.ReadAsync(ct)) throw new UnauthorizedAccessException("Gateway enrollment is revoked or the physical drone/owner does not match. Pair again.");
            ownerId = reader.GetGuid(0); gatewayName = reader.GetString(1);
            systemId = reader.GetInt32(2); componentId = reader.GetInt32(3);
        }
        var leaseToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        await using var lease = new NpgsqlCommand("""
            UPDATE public."DroneConnection" c SET "TokenHash" = @hash, "HardwareIdentity" = @identity,
                "PairedAt" = COALESCE(c."PairedAt", now()), "LastGatewayAt" = now(),
                "ExpiresAt" = now() + interval '12 hours',
                "GatewayName" = @name, "SystemId" = @system, "ComponentId" = @component
            WHERE c."Id" = (
                SELECT d."Id" FROM public."DroneConnection" d
                JOIN public."Mission" m ON m."Id" = d."MissionId" AND m."UAVId" = d."UavId"
                    AND m."CreatedBy" = d."OwnerId" AND m."Status" IN ('Approved', 'InProgress', 'In Progress')
                WHERE d."UavId" = @uav AND d."OwnerId" = @owner AND d."RevokedAt" IS NULL
                    AND (d."HardwareIdentity" IS NULL OR d."HardwareIdentity" = @identity)
                    AND m."EndTime" > now()
                ORDER BY d."CreatedAt" DESC LIMIT 1 FOR UPDATE OF d
            ) AND c."RevokedAt" IS NULL
            RETURNING c."Id", c."MissionId", c."ExpiresAt"
            """, connection, transaction);
        lease.Parameters.AddWithValue("name", gatewayName);
        lease.Parameters.AddWithValue("system", systemId);
        lease.Parameters.AddWithValue("component", componentId);
        lease.Parameters.AddWithValue("hash", DroneConnectionService.Hash(leaseToken));
        lease.Parameters.AddWithValue("identity", identity);
        lease.Parameters.AddWithValue("uav", uavId);
        lease.Parameters.AddWithValue("owner", ownerId);
        GatewayCredential? result = null;
        await using (var reader = await lease.ExecuteReaderAsync(ct))
        {
            if (await reader.ReadAsync(ct))
            {
                var id = reader.GetGuid(0);
                result = new(id, uavId, reader.GetGuid(1), $"gateway-{id:N}", leaseToken,
                    reader.GetFieldValue<DateTimeOffset>(2), identity);
            }
        }
        await transaction.CommitAsync(ct);
        return result;
    }

    public async Task RevokeAsync(Guid uav, Guid owner, CancellationToken ct)
    {
        await using var command = source.CreateCommand("""
            WITH revoked AS (
                UPDATE public."GatewayEnrollment" SET "RevokedAt" = now()
                WHERE "UavId" = @uav AND "OwnerId" = @owner RETURNING "UavId"
            ) UPDATE public."DroneConnection" SET "RevokedAt" = now()
                WHERE "UavId" IN (SELECT "UavId" FROM revoked) AND "OwnerId" = @owner AND "RevokedAt" IS NULL
            """);
        command.Parameters.AddWithValue("uav", uav);
        command.Parameters.AddWithValue("owner", owner);
        await command.ExecuteNonQueryAsync(ct);
    }
}
