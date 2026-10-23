using System.ComponentModel.DataAnnotations.Schema;

namespace DroneOps.Domain.Entities;

[Table("Notification")]
public sealed class Notification
{
    public Guid Id { get; set; }
    public Guid RecipientUserId { get; set; }
    public Guid? ActorUserId { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public Guid? ReferenceId { get; set; }
    public string? ReferenceType { get; set; }
    public string? ActionUrl { get; set; }
    public bool IsRead { get; set; }
    public DateTimeOffset? ReadAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public User Recipient { get; set; } = null!;
    public User? Actor { get; set; }
}
