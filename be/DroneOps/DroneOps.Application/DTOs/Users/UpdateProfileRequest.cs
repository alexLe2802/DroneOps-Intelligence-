using System.ComponentModel.DataAnnotations;

namespace DroneOps.Application.DTOs.Users;

public sealed class UpdateProfileRequest
{
    [Required(ErrorMessage = "Full name is required.")]
    [StringLength(
        100,
        MinimumLength = 2,
        ErrorMessage =
            "Full name must be between 2 and 100 characters.")]
    public string FullName { get; set; } = string.Empty;
}