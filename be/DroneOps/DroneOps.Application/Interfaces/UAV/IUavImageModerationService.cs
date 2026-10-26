namespace DroneOps.Application.Interfaces.UAVs;

public interface IUavImageModerationService
{
    Task<bool> IsAllowedAsync(
        byte[] imageBytes,
        string contentType,
        CancellationToken cancellationToken = default);
}
