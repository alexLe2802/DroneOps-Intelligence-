namespace DroneOps.Domain.Entities;

public class User
{
    public Guid Id { get; set; }

    public Guid RoleId { get; set; }

    public string FullName { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public string PasswordHash { get; set; } = string.Empty;
}