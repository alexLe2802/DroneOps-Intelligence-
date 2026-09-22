using System.ComponentModel.DataAnnotations;

namespace DroneOps.Application.DTOs.Auth;

public class VerifyRegisterRequest
{
    [Required(ErrorMessage = "Email is required.")]
    [RegularExpression(@"^[a-zA-Z0-9._%+-]+@(?!.*(\.[a-zA-Z]{2,})\1$)[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}$",
        ErrorMessage = "Invalid email address format.")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Verification code is required.")]
    [StringLength(5, MinimumLength = 5, ErrorMessage = "Verification code must be exactly 5 characters.")]
    public string Code { get; set; } = string.Empty;
}