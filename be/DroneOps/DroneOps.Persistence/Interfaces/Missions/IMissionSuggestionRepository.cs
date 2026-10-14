using DroneOps.Application.Interfaces;
using DroneOps.Domain.Entities;

namespace DroneOps.Application.Interfaces.Missions;

public interface IMissionSuggestionRepository
    : IGenericRepository<MissionSuggestion>
{
    Task<Guid?> GetPilotIdByUserIdAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<List<MissionSuggestion>> GetByPilotIdAsync(
        Guid pilotId,
        CancellationToken cancellationToken = default);

    Task<MissionSuggestion?> GetOwnedByIdAsync(
        Guid suggestionId,
        Guid pilotId,
        CancellationToken cancellationToken = default);
}