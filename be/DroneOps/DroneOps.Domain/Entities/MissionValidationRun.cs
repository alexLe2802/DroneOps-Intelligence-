using System.ComponentModel.DataAnnotations.Schema;
namespace DroneOps.Domain.Entities;

[Table("MissionValidationRun")]
public sealed class MissionValidationRun
{
    public Guid Id { get; set; }
    public Guid? MissionId { get; set; }
    public Guid? UpdateRequestId { get; set; }
    public Guid ActorId { get; set; }
    public string Stage { get; set; } = "Creation";
    public bool IsValid { get; set; }
    public string ResultStatus { get; set; } = "Error";
    public DateTimeOffset CheckedAt { get; set; }
    public string PlanHash { get; set; } = "";
    public string RestrictionRevision { get; set; } = "";
    [Column(TypeName = "jsonb")]
    public string EvidenceJson { get; set; } = "{}";
    [Column(TypeName = "jsonb")]
    public string ResultJson { get; set; } = "{}";
    [Column(TypeName = "jsonb")]
    public string PlanJson { get; set; } = "{}";
}
