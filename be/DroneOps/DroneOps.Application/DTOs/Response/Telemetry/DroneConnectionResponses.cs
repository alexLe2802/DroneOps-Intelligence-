namespace DroneOps.Application.DTOs.Response.Telemetry;

public record DroneConnectionView(Guid Id, Guid UavId, Guid MissionId, DateTimeOffset CreatedAt, DateTimeOffset CodeExpiresAt,
    DateTimeOffset ExpiresAt, DateTimeOffset? PairedAt, DateTimeOffset? LastGatewayAt, DateTimeOffset? LastSampleAt,
    DateTimeOffset? RevokedAt, string? GatewayName, int SystemId, int ComponentId, string Status, string? HardwareIdentity);
public record DronePairing(DroneConnectionView Connection, string PairingCode);
public record GatewayCredential(Guid ConnectionId, Guid UavId, Guid MissionId, string SourceId, string Token, DateTimeOffset ExpiresAt, string HardwareIdentity);

public record DroneBindingView(Guid UavId, string HardwareIdentity, DateTimeOffset BoundAt);
