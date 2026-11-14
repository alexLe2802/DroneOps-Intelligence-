using System.ComponentModel.DataAnnotations.Schema;
namespace DroneOps.Domain.Entities;

[Table("UavReviewRequest")]
public sealed class UavReviewRequest
{
    public Guid Id { get; set; }
    public Guid UavId { get; set; }
    public Guid? SubmittedBy { get; set; }
    public DateTimeOffset? SubmittedAt { get; set; }
    public long BaseVersion { get; set; }
    public string Kind { get; set; } = "Update";
    public string Status { get; set; } = "Pending";
    public string BaseInformationJson { get; set; } = "{}";
    public string ProposedInformationJson { get; set; } = "{}";
    public Guid? ReviewedBy { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }
    public string? Reason { get; set; }
}
