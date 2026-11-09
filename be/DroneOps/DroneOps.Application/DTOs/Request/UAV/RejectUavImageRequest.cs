using System.ComponentModel.DataAnnotations;

namespace DroneOps.Application.DTOs.Request.UAVs;

public sealed class RejectUavImageRequest
{
    [Required]
    [MaxLength(500)]
    public string Reason { get; set; } = string.Empty;
}
