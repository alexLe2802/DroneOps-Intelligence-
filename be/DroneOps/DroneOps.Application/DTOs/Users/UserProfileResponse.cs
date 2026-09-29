namespace DroneOps.Application.DTOs.Users;

public sealed class UserProfileResponse
{
    public Guid Id { get; set; }

    public string FullName { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public string Role { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }
}