using System.ComponentModel.DataAnnotations.Schema;

namespace DroneOps.Domain.Entities;

[Table("RegistryDocument")]
public sealed class RegistryDocument
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? MissionId { get; set; }
    public Guid? UavId { get; set; }
    public Guid UploadedBy { get; set; }
    public DateTimeOffset UploadedAt { get; set; } = DateTimeOffset.UtcNow;
    public string Name { get; set; } = "";
    public byte[] Content { get; set; } = [];
    public string? Decision { get; set; }
    public Guid? ReviewedBy { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }
    public string? Reason { get; set; }
}
