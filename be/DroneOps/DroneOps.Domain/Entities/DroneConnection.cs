using System.ComponentModel.DataAnnotations.Schema;
namespace DroneOps.Domain.Entities;

[Table("DroneConnection")]
public sealed class DroneConnection
{
    public Guid Id { get; set; }
    public Guid OwnerId { get; set; }
    public Guid UavId { get; set; }
    public Guid MissionId { get; set; }
    public string PairCodeHash { get; set; } = "";
    public string? HardwareIdentity { get; set; }
    public string? TokenHash { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset CodeExpiresAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? PairedAt { get; set; }
    public DateTimeOffset? LastGatewayAt { get; set; }
    public DateTimeOffset? LastSampleAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public string? GatewayName { get; set; }
    public int SystemId { get; set; }
    public int ComponentId { get; set; }
}
