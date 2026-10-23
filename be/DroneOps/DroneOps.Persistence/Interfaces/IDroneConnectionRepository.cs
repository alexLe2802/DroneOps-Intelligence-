using DroneOps.Domain.Entities;
namespace DroneOps.Persistence.Interfaces;

public interface IDroneConnectionRepository
{
    Task<List<DroneHardwareIdentity>> BindingsAsync(Guid owner, CancellationToken ct);
    Task CreateAsync(DroneConnection connection, CancellationToken ct);
    Task<List<DroneConnection>> ListAsync(Guid ownerId, CancellationToken ct);
    Task<bool> RevokeAsync(Guid id, Guid ownerId, CancellationToken ct);
    Task<DroneConnection?> ClaimAsync(string codeHash, string tokenHash, string name, int systemId, int componentId, string hardwareIdentity, CancellationToken ct);
    Task<DroneConnection?> AuthenticateAsync(Guid id, string tokenHash, CancellationToken ct);
    Task TouchAsync(Guid id, bool sample, CancellationToken ct);
}
