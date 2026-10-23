using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
namespace DroneOps.Domain.Entities;

[Table("DroneHardwareIdentity")]
public sealed class DroneHardwareIdentity
{
    [Key] public Guid UavId { get; set; }
    public string Identity { get; set; } = "";
    public Guid BoundBy { get; set; }
    public DateTimeOffset BoundAt { get; set; }
    public UAV Uav { get; set; } = null!;
}
