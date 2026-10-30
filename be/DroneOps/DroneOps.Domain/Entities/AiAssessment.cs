using System.ComponentModel.DataAnnotations.Schema;

namespace DroneOps.Domain.Entities;

[Table("ai_assessments", Schema = "droneops")]
public sealed class AiAssessment
{
    [Column("id")] public Guid Id { get; set; }
    [Column("mission_ref")] public string MissionRef { get; set; } = string.Empty;
    [Column("mission_version")] public int MissionVersion { get; set; }
    [Column("requested_by")] public Guid RequestedBy { get; set; }
    [Column("provider")] public string Provider { get; set; } = string.Empty;
    [Column("model")] public string Model { get; set; } = string.Empty;
    [Column("context_hash")] public string ContextHash { get; set; } = string.Empty;
    [Column("context_snapshot", TypeName = "jsonb")] public string ContextSnapshot { get; set; } = "{}";
    [Column("status")] public string Status { get; set; } = "Pending";
    [Column("result", TypeName = "jsonb")] public string? Result { get; set; }
    [Column("error_code")] public string? ErrorCode { get; set; }
    [Column("created_at")] public DateTimeOffset CreatedAt { get; set; }
    [Column("completed_at")] public DateTimeOffset? CompletedAt { get; set; }
}
