namespace DroneOps.Application.DTOs.Response.Auth;

public class LoginResponse
{
    public bool RequiresFirstLogin { get; set; }

    public string AccessToken { get; set; } = string.Empty;

    public DateTime ExpiresAt { get; set; }

    public LoginUserResponse User { get; set; } = new();
}

public class LoginUserResponse
{
    public Guid Id { get; set; }

    public string FullName { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public string Role { get; set; } = string.Empty;
}