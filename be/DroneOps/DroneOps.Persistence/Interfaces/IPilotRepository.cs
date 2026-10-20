using DroneOps.Application.Interfaces;
using DroneOps.Domain.Entities;

namespace DroneOps.Persistence.Interfaces;

public interface IPilotRepository
    : IGenericRepository<Pilot>
{
    Task<Pilot?> GetByUserIdAsync(
        Guid userId,
        CancellationToken cancellationToken = default);
    Task<Pilot?> GetByUserIdForUpdateAsync(
     Guid userId,
     CancellationToken cancellationToken = default);
    Task<bool> HasPendingUpdateRequestAsync(
    Guid pilotId,
    CancellationToken cancellationToken = default);
    Task AddUpdateRequestAsync(
        PilotUpdateRequest request,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PilotUpdateRequest>> GetPendingUpdateRequestsAsync(
          CancellationToken cancellationToken = default);
    Task<PilotUpdateRequest?>GetUpdateRequestForReviewAsync(
            Guid requestId,
            CancellationToken cancellationToken = default);
    Task<List<LicenseImage>>GetCurrentPilotLicenseImagesAsync(
        Guid pilotId,
        CancellationToken cancellationToken = default);
}