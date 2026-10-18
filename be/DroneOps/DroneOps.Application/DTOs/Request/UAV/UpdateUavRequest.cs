using System.ComponentModel.DataAnnotations;

namespace DroneOps.Application.DTOs.Request.UAVs;

public sealed class UpdateUavRequest
{
    [Required(ErrorMessage = "UAV code is required.")]
    [StringLength(50, ErrorMessage = "UAV code must not exceed 50 characters.")]
    public string Code { get; set; } = string.Empty;

    [Required(ErrorMessage = "UAV name is required.")]
    [StringLength(100, ErrorMessage = "UAV name must not exceed 100 characters.")]
    public string Name { get; set; } = string.Empty;

    [StringLength(100, ErrorMessage = "UAV model must not exceed 100 characters.")]
    public string? Model { get; set; }
}
