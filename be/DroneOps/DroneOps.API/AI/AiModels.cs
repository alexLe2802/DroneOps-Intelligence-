using System.ComponentModel.DataAnnotations;

namespace DroneOps.API.AI;

public sealed record MissionWaypointInput(
    [Range(-90, 90)] decimal Latitude,
    [Range(-180, 180)] decimal Longitude,
    [Range(0, 10000)] decimal Altitude,
    [StringLength(40)] string Action);

public sealed record CreateAiAssessmentRequest(
    [Required, StringLength(80)] string MissionRef,
    [Required, StringLength(160)] string MissionName,
    [Range(1, 100000)] int MissionVersion,
    [Required, StringLength(40)] string MissionStatus,
    [StringLength(80)] string? UavCode,
    DateTimeOffset? StartTime,
    DateTimeOffset? EndTime,
    [MinLength(2), MaxLength(100)] IReadOnlyList<MissionWaypointInput> Waypoints,
    [Required, StringLength(2000)] string ValidationSummary,
    [MaxLength(20)] IReadOnlyList<string> Incidents,
    [StringLength(2000)] string? TelemetrySummary);

public sealed record AiRiskItem(string Severity, string Title, string Evidence, string Precaution);
public sealed record AiAssessmentResult(string Summary, IReadOnlyList<AiRiskItem> Risks,
    IReadOnlyList<string> Precautions, IReadOnlyList<string> MissingData, string Disclaimer);

public sealed record AiAssessmentResponse(Guid Id, string MissionRef, int MissionVersion,
    string Provider, string Model, string Status, AiAssessmentResult? Result,
    string? ErrorCode, DateTimeOffset CreatedAt, DateTimeOffset? CompletedAt);
