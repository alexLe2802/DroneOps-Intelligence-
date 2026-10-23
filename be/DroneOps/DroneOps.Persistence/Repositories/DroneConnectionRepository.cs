using System.Data;
using DroneOps.Domain.Entities;
using DroneOps.Persistence.Data;
using DroneOps.Persistence.Interfaces;
using Microsoft.EntityFrameworkCore;
namespace DroneOps.Persistence.Repositories;

public sealed class DroneConnectionRepository(AppDbContext db) : IDroneConnectionRepository
{
    private IQueryable<DroneConnection> Usable() => db.DroneConnections.Where(c => c.RevokedAt == null && c.ExpiresAt > DateTimeOffset.UtcNow
        && db.UAVs.Any(u => u.Id == c.UavId && u.Status == "Active" && u.Pilot.UserId == c.OwnerId)
        && db.Missions.Any(m => m.Id == c.MissionId && m.UAVId == c.UavId && m.CreatedBy == c.OwnerId
            && (m.Status == "Approved" || m.Status == "InProgress" || m.Status == "In Progress")));

    public Task CreateAsync(DroneConnection connection, CancellationToken ct) => db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
    {
        db.ChangeTracker.Clear();
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        // Lock the aircraft: two browser tabs cannot create competing active sessions.
        var aircraft = await db.UAVs.FromSqlInterpolated($"SELECT * FROM \"UAV\" WHERE \"Id\"={connection.UavId} FOR UPDATE").SingleOrDefaultAsync(ct);
        if (aircraft is null || !await db.Pilots.AnyAsync(p => p.Id == aircraft.PilotId && p.UserId == connection.OwnerId, ct))
            throw new UnauthorizedAccessException("This UAV is not assigned to your pilot account.");
        if (aircraft.Status != "Active") throw new InvalidOperationException("The UAV must be active before connecting.");
        if (!await db.Missions.AnyAsync(m => m.Id == connection.MissionId && m.CreatedBy == connection.OwnerId && m.UAVId == aircraft.Id
            && (m.Status == "Approved" || m.Status == "InProgress" || m.Status == "In Progress"), ct))
            throw new InvalidOperationException("Select your approved or in-progress mission assigned to this UAV.");
        await db.DroneConnections.Where(c => c.UavId == aircraft.Id && c.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.RevokedAt, DateTimeOffset.UtcNow), ct);
        db.DroneConnections.Add(connection);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    });

    public Task<List<DroneConnection>> ListAsync(Guid ownerId, CancellationToken ct) => db.DroneConnections.AsNoTracking()
        .Where(c => c.OwnerId == ownerId).OrderByDescending(c => c.CreatedAt).Take(30).ToListAsync(ct);
    public async Task<bool> RevokeAsync(Guid id, Guid ownerId, CancellationToken ct) => await db.DroneConnections
        .Where(c => c.Id == id && c.OwnerId == ownerId).ExecuteUpdateAsync(s => s.SetProperty(c => c.RevokedAt, DateTimeOffset.UtcNow), ct) > 0;
    public Task<List<DroneHardwareIdentity>> BindingsAsync(Guid owner,CancellationToken ct) => db.DroneHardwareIdentities.AsNoTracking()
        .Where(h=>h.Uav.Pilot.UserId==owner).ToListAsync(ct);

    public Task<DroneConnection?> ClaimAsync(string codeHash,string tokenHash,string name,int systemId,int componentId,string hardwareIdentity,CancellationToken ct)
        => db.Database.CreateExecutionStrategy().ExecuteAsync<DroneConnection?>(async () =>
    {
        db.ChangeTracker.Clear();
        await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,ct);
        var c=await Usable().AsNoTracking().SingleOrDefaultAsync(c=>c.PairCodeHash==codeHash&&c.TokenHash==null&&c.CodeExpiresAt>DateTimeOffset.UtcNow,ct);
        if(c is null)return null;
        // Serialize competing first bindings, including two different registry UAVs
        // claiming the same hardware. The unique index is the final DB invariant.
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({hardwareIdentity},0))",ct);
        await db.UAVs.FromSqlInterpolated($"SELECT * FROM \"UAV\" WHERE \"Id\"={c.UavId} FOR UPDATE").SingleAsync(ct);
        var bound=await db.DroneHardwareIdentities.AsNoTracking().SingleOrDefaultAsync(h=>h.UavId==c.UavId,ct);
        if(bound is not null && bound.Identity!=hardwareIdentity)
            throw new InvalidOperationException("The connected drone does not match this registered UAV. Select the UAV already bound to this hardware.");
        if(await db.DroneHardwareIdentities.AnyAsync(h=>h.Identity==hardwareIdentity&&h.UavId!=c.UavId,ct))
            throw new InvalidOperationException("This physical drone is already bound to another registered UAV. It cannot be linked to multiple registry records.");
        if(bound is null)db.DroneHardwareIdentities.Add(new(){UavId=c.UavId,Identity=hardwareIdentity,BoundBy=c.OwnerId,BoundAt=DateTimeOffset.UtcNow});
        var now=DateTimeOffset.UtcNow;
        var changed=await Usable().Where(x=>x.Id==c.Id&&x.TokenHash==null&&x.CodeExpiresAt>now)
            .ExecuteUpdateAsync(s=>s.SetProperty(x=>x.TokenHash,tokenHash).SetProperty(x=>x.HardwareIdentity,hardwareIdentity)
                .SetProperty(x=>x.PairedAt,now).SetProperty(x=>x.LastGatewayAt,now).SetProperty(x=>x.GatewayName,name)
                .SetProperty(x=>x.SystemId,systemId).SetProperty(x=>x.ComponentId,componentId),ct);
        if(changed!=1)return null;
        try { await db.SaveChangesAsync(ct); }
        catch(DbUpdateException e) when(e.InnerException is Npgsql.PostgresException {SqlState:"23505"})
        { throw new InvalidOperationException("This hardware or UAV was bound by another pairing. Refresh and select the matching UAV.",e); }
        await tx.CommitAsync(ct);
        return await db.DroneConnections.AsNoTracking().SingleAsync(x=>x.Id==c.Id,ct);
    });
    public Task<DroneConnection?> AuthenticateAsync(Guid id, string tokenHash, CancellationToken ct) => Usable().AsNoTracking()
        .SingleOrDefaultAsync(c => c.Id == id && c.TokenHash == tokenHash && c.PairedAt != null && c.HardwareIdentity != null
            && db.DroneHardwareIdentities.Any(h=>h.UavId==c.UavId&&h.Identity==c.HardwareIdentity), ct);
    public Task TouchAsync(Guid id, bool sample, CancellationToken ct)
    {
        var query = db.DroneConnections.Where(c => c.Id == id && c.RevokedAt == null);
        return sample ? query.ExecuteUpdateAsync(s => s.SetProperty(c => c.LastGatewayAt, DateTimeOffset.UtcNow).SetProperty(c => c.LastSampleAt, DateTimeOffset.UtcNow), ct)
            : query.ExecuteUpdateAsync(s => s.SetProperty(c => c.LastGatewayAt, DateTimeOffset.UtcNow), ct);
    }
}
