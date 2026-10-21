using System.ComponentModel.DataAnnotations.Schema;

namespace DroneOps.Domain.Entities;

// Ánh xạ chính xác vào bảng "MissionApproval" (dạng số ít) trong PostgreSQL
[Table("MissionApproval")]
public class MissionApproval
{
    public Guid Id { get; set; }

    public Guid MissionId { get; set; }

    public Guid? UpdateRequestId { get; set; }
    public Guid? ValidationRunId { get; set; }

    public Guid UserId { get; set; }

    public string Status { get; set; } = "Pending";

    public string? Comment { get; set; }

    public DateTimeOffset? ApprovedAt { get; set; }

    [ForeignKey(nameof(MissionId))]
    public Mission Mission { get; set; } = null!;

    [ForeignKey(nameof(UserId))]
    public User User { get; set; } = null!;
}