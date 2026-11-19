using DroneOps.Application.DTOs.Response;

namespace DroneOps.Application.Interfaces;

public interface IAIMissionReviewService
{
    Task<AIMissionReviewResponse> ReviewMissionAsync(
        Guid missionId,
        CancellationToken cancellationToken = default);
}
