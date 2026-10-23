using System.Security.Cryptography;
using System.Text;
using DroneOps.Application.DTOs.Request.Telemetry;
using DroneOps.Application.DTOs.Response.Telemetry;
using DroneOps.Application.Interfaces.Telemetry;
using DroneOps.Domain.Entities;
using DroneOps.Persistence.Interfaces;
namespace DroneOps.Application.Services.Telemetry;

public sealed class DroneConnectionService(IDroneConnectionRepository repository, ITelemetryIngestionService telemetry) : IDroneConnectionService
{
    public static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    public static DroneConnectionView View(DroneConnection c)
    {
        var now = DateTimeOffset.UtcNow;
        var state = c.RevokedAt != null ? "Revoked" : c.ExpiresAt <= now ? "Expired" : c.PairedAt == null ? c.CodeExpiresAt <= now ? "PairingExpired" : "AwaitingGateway"
            : c.LastGatewayAt < now.AddSeconds(-30) ? "Offline" : c.LastSampleAt >= now.AddSeconds(-15) ? "ReceivingTelemetry" : "GatewayConnected";
        return new(c.Id,c.UavId,c.MissionId,c.CreatedAt,c.CodeExpiresAt,c.ExpiresAt,c.PairedAt,c.LastGatewayAt,c.LastSampleAt,c.RevokedAt,c.GatewayName,c.SystemId,c.ComponentId,state,c.HardwareIdentity);
    }
    public async Task<List<DroneBindingView>> BindingsAsync(Guid owner,CancellationToken ct) => (await repository.BindingsAsync(owner,ct)).Select(h=>new DroneBindingView(h.UavId,h.Identity,h.BoundAt)).ToList();
    public async Task<DronePairing> CreateAsync(Guid owner, CreateDroneConnection request, CancellationToken ct)
    {
        if (request.UavId == Guid.Empty || request.MissionId == Guid.Empty) throw new ArgumentException("Select a UAV and mission.");
        var code = Convert.ToHexString(RandomNumberGenerator.GetBytes(8)); // 64-bit, expires in five minutes.
        var now = DateTimeOffset.UtcNow;
        var connection = new DroneConnection { Id=Guid.NewGuid(),OwnerId=owner,UavId=request.UavId,MissionId=request.MissionId,
            PairCodeHash=Hash(code),CreatedAt=now,CodeExpiresAt=now.AddMinutes(5),ExpiresAt=now.AddHours(12) };
        await repository.CreateAsync(connection,ct);
        return new(View(connection),code);
    }
    public async Task<List<DroneConnectionView>> ListAsync(Guid owner,CancellationToken ct) => (await repository.ListAsync(owner,ct)).Select(View).ToList();
    public Task<bool> RevokeAsync(Guid id,Guid owner,CancellationToken ct) => repository.RevokeAsync(id,owner,ct);
    public async Task<GatewayCredential> PairAsync(PairDroneGateway request,CancellationToken ct)
    {
        var code=(request.Code??"").Replace("-","").Replace(" ","").ToUpperInvariant();
        if(code.Length!=16 || code.Any(c=>!Uri.IsHexDigit(c)) || string.IsNullOrWhiteSpace(request.GatewayName) || request.GatewayName.Length>80
            || request.SystemId is <1 or >255 || request.ComponentId is <1 or >255) throw new ArgumentException("Invalid pairing code, gateway name or MAVLink system/component ID.");
        var identity=(request.HardwareIdentity??"").ToUpperInvariant();
        if(!System.Text.RegularExpressions.Regex.IsMatch(identity,@"^(UID64-[0-9A-F]{16}|UID2-[0-9A-F]{36})$") || identity[(identity.IndexOf('-')+1)..].All(c=>c=='0'))
            throw new ArgumentException("A non-zero AUTOPILOT_VERSION hardware UID is required. MAVLink system ID, COM port and gateway name are not device identities.");
        var token=Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var c=await repository.ClaimAsync(Hash(code),Hash(token),request.GatewayName.Trim(),request.SystemId,request.ComponentId,identity,ct)
            ?? throw new UnauthorizedAccessException("Pairing code is invalid, expired or already used. Create a new code on the web.");
        return new(c.Id,c.UavId,c.MissionId,$"gateway-{c.Id:N}",token,c.ExpiresAt,c.HardwareIdentity!);
    }
    private async Task<DroneConnection> Authenticate(Guid id,string token,string hardwareIdentity,CancellationToken ct)
    {
        if(token.Length!=64)throw new UnauthorizedAccessException("Invalid gateway credential.");
        var c=await repository.AuthenticateAsync(id,Hash(token),ct) ?? throw new UnauthorizedAccessException("Connection expired, revoked or no longer assigned to an eligible mission/UAV.");
        if(c.HardwareIdentity!=hardwareIdentity)throw new UnauthorizedAccessException("Connected drone hardware does not match this session.");
        return c;
    }
    public async Task HeartbeatAsync(Guid id,string token,string hardwareIdentity,CancellationToken ct)
    { await Authenticate(id,token,hardwareIdentity,ct); await repository.TouchAsync(id,false,ct); }
    public async Task<TelemetryIngestResult> SampleAsync(Guid id,string token,string hardwareIdentity,string key,IngestTelemetrySampleRequest request,CancellationToken ct)
    {
        var c=await Authenticate(id,token,hardwareIdentity,ct);
        var source=$"gateway-{c.Id:N}";
        if(request.UavId!=c.UavId || request.MissionId!=c.MissionId || request.SourceId!=source)
            throw new UnauthorizedAccessException("Gateway credential is restricted to its paired UAV and mission.");
        if(string.IsNullOrWhiteSpace(key) || key.Length>150)throw new ArgumentException("A valid Idempotency-Key is required.");
        var result=await telemetry.IngestSampleAsync($"{source}:{Hash(key)}",request,ct);
        if(result.Accepted && result.UsedForLatestProjection && !result.IsDuplicate && !result.IsLateArrival)await repository.TouchAsync(id,true,ct);
        return result;
    }
}
