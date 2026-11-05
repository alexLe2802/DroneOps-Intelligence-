namespace DroneOps.Application.Interfaces.UAVs;

public enum UavImageModerationDecision
{
    Approved,
    Rejected,
    Unavailable
}

public sealed record UavImageModerationResult(
    UavImageModerationDecision Decision,
    string Reason);

public interface IUavImageModerationService
{
    Task<UavImageModerationResult> ModerateAsync(
        byte[] imageBytes,
        string contentType,
        CancellationToken cancellationToken = default);
}
