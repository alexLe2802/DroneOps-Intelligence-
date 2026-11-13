namespace DroneOps.Application.DTOs.Request.Missions;

public sealed class SubmitMissionRequest
{
    public Guid OperationId { get; set; }
}

public sealed class ConfirmMissionRiskRequest
{
    public Guid UpdateRequestId { get; set; }
    public Guid ValidationRunId { get; set; }
}
