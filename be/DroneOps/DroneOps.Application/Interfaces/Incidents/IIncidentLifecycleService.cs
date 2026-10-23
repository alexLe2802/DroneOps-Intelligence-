using DroneOps.Domain.Entities;

namespace DroneOps.Application.Interfaces.Incidents;

public interface IIncidentLifecycleService
{
    Task<Guid?> RaiseIfOpenMissingAsync(Incident incident, CancellationToken cancellationToken = default);

    Task<bool> ResolveOpenByRuleAsync(
        Guid missionId,
        string type,
        string notes,
        CancellationToken cancellationToken = default);

    Task AppendTransitionAndAlertsAsync(
        Incident incident,
        string fromStatus,
        string toStatus,
        string eventType,
        string? notes,
        Guid? changedBy,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken = default);
}
