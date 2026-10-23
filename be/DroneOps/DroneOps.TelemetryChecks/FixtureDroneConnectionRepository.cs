using DroneOps.Domain.Entities;
using DroneOps.Persistence.Interfaces;

// Test double only: PostgreSQL repository semantics are verified by AuthChecks.
internal sealed class FixtureDroneConnectionRepository : IDroneConnectionRepository
{
    private readonly Dictionary<Guid,DroneConnection> rows=[];
    private readonly Dictionary<Guid,DroneHardwareIdentity> hardware=[];
    public Task<List<DroneHardwareIdentity>> BindingsAsync(Guid owner,CancellationToken ct) {lock(gate)return Task.FromResult(hardware.Values.Where(h=>h.BoundBy==owner).ToList());}
    private readonly object gate=new();
    public Task CreateAsync(DroneConnection c,CancellationToken ct)
    {lock(gate){foreach(var row in rows.Values.Where(x=>x.UavId==c.UavId&&x.RevokedAt==null))row.RevokedAt=DateTimeOffset.UtcNow;rows.Add(c.Id,c);}return Task.CompletedTask;}
    public Task<List<DroneConnection>> ListAsync(Guid owner,CancellationToken ct)
    {lock(gate)return Task.FromResult(rows.Values.Where(c=>c.OwnerId==owner).ToList());}
    public Task<bool> RevokeAsync(Guid id,Guid owner,CancellationToken ct)
    {lock(gate){if(!rows.TryGetValue(id,out var row)||row.OwnerId!=owner)return Task.FromResult(false);row.RevokedAt=DateTimeOffset.UtcNow;return Task.FromResult(true);}}
    public Task<DroneConnection?> ClaimAsync(string hash,string token,string name,int sys,int comp,string identity,CancellationToken ct)
    {lock(gate){var row=rows.Values.SingleOrDefault(c=>c.PairCodeHash==hash&&c.TokenHash==null&&c.RevokedAt==null&&c.CodeExpiresAt>DateTimeOffset.UtcNow);if(row!=null){if(hardware.TryGetValue(row.UavId,out var h)&&h.Identity!=identity)throw new InvalidOperationException("Wrong hardware");if(hardware.Values.Any(h=>h.Identity==identity&&h.UavId!=row.UavId))throw new InvalidOperationException("Hardware already bound");hardware.TryAdd(row.UavId,new(){UavId=row.UavId,Identity=identity,BoundBy=row.OwnerId,BoundAt=DateTimeOffset.UtcNow});row.HardwareIdentity=identity;row.TokenHash=token;row.GatewayName=name;row.PairedAt=row.LastGatewayAt=DateTimeOffset.UtcNow;row.SystemId=sys;row.ComponentId=comp;}return Task.FromResult(row);}}
    public Task<DroneConnection?> AuthenticateAsync(Guid id,string token,CancellationToken ct)
    {lock(gate)return Task.FromResult(rows.Values.SingleOrDefault(c=>c.Id==id&&c.TokenHash==token&&c.RevokedAt==null&&c.ExpiresAt>DateTimeOffset.UtcNow));}
    public Task TouchAsync(Guid id,bool sample,CancellationToken ct)
    {lock(gate){var row=rows[id];row.LastGatewayAt=DateTimeOffset.UtcNow;if(sample)row.LastSampleAt=row.LastGatewayAt;}return Task.CompletedTask;}
}
