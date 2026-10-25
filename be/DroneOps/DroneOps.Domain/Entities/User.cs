using System.ComponentModel.DataAnnotations.Schema;

namespace DroneOps.Domain.Entities;

[Table("User")]
public class User
{
    public Guid Id { get; set; }

    public string? AvatarPath { get; set; }

    public Guid RoleId { get; set; }

    public string FullName { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public string PasswordHash { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    public bool RequiresFirstLogin { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Role Role { get; set; } = null!;
}
