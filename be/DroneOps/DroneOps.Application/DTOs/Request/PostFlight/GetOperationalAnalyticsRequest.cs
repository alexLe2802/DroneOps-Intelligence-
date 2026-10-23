namespace DroneOps.Application.DTOs.Request.PostFlight;

public sealed class GetOperationalAnalyticsRequest
{
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public Guid? UavId { get; set; }
    public Guid? MissionId { get; set; }
}
