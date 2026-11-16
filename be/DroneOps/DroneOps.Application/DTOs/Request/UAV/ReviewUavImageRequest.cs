using System.ComponentModel.DataAnnotations;

namespace DroneOps.Application.DTOs.Request.UAV;

public sealed class ReviewUavImageRequest
{
    [Required, StringLength(500)]
    public string Reason { get; set; } = string.Empty;
}
