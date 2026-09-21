namespace DroneOps.Domain.Entities;

public class MissionApproval
{
    public Guid Id { get; set; }

    public Guid MissionId { get; set; }

    public Guid UserId { get; set; }

    public string Status { get; set; } = "Pending";

    public string? Comment { get; set; }

    public DateTimeOffset? ApprovedAt { get; set; }

    public Mission Mission { get; set; } = null!;

    public User User { get; set; } = null!;
}