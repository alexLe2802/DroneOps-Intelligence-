namespace DroneOps.Application.DTOs.Response;

public class AIMissionReviewResponse
{
    public Guid MissionId { get; set; }

    public string MissionName { get; set; } = string.Empty;

    public string Advice { get; set; } = string.Empty;
}
