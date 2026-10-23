namespace DroneOps.Application.DTOs.Request.Telemetry;

public record CreateDroneConnection(Guid UavId, Guid MissionId);
public record PairDroneGateway(string Code, string GatewayName, int SystemId, int ComponentId = 1, string HardwareIdentity = "");
