using DroneOps.Application.DTOs.Request.Telemetry;
using DroneOps.Application.DTOs.Response.Telemetry;
namespace DroneOps.Application.Interfaces.Telemetry;

public interface IDroneConnectionService
{
    Task<List<DroneBindingView>> BindingsAsync(Guid owner, CancellationToken ct);
    Task<DronePairing> CreateAsync(Guid owner, CreateDroneConnection request, CancellationToken ct);
    Task<List<DroneConnectionView>> ListAsync(Guid owner, CancellationToken ct);
    Task<bool> RevokeAsync(Guid id, Guid owner, CancellationToken ct);
    Task<GatewayCredential> PairAsync(PairDroneGateway request, CancellationToken ct);
    Task HeartbeatAsync(Guid id, string token, string hardwareIdentity, CancellationToken ct);
    Task<TelemetryIngestResult> SampleAsync(Guid id, string token, string hardwareIdentity, string key, IngestTelemetrySampleRequest request, CancellationToken ct);
}
