using DroneOps.Domain.Entities;

namespace DroneOps.Persistence.Interfaces;

public interface IMissionValidationRepository
{
    Task<Mission?> GetMissionWithWaypointsAsync(
        Guid missionId,
        bool tracking = false,
        CancellationToken cancellationToken = default);

    Task<Mission?> GetMissionAsync(
        Guid missionId,
        bool tracking = false,
        CancellationToken cancellationToken = default);

    Task<MissionValidationRun?> GetLatestRunAsync(
        Guid missionId,
        CancellationToken cancellationToken = default);

    Task<MissionValidationRun?> GetLatestPreflightRunAsync(
        Guid missionId,
        CancellationToken cancellationToken = default);

    Task<MissionValidationRun?> GetRunAsync(
        Guid missionId,
        Guid runId,
        CancellationToken cancellationToken = default);

    Task<MissionValidationRun?> GetRecentPreflightRunAsync(
        Guid missionId,
        DateTimeOffset checkedAfter,
        CancellationToken cancellationToken = default);

    Task<List<Geofence>> GetGeofencesAsync(
        CancellationToken cancellationToken = default);

    Task<List<(Guid Id, long Revision)>> GetGeofenceRevisionsAsync(
        CancellationToken cancellationToken = default);

    Task<Pilot?> GetPilotByUserIdAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<UAV?> GetUavByIdAsync(
        Guid uavId,
        CancellationToken cancellationToken = default);

    Task<bool> HasScheduleConflictAsync(
        Guid missionId,
        Guid? uavId,
        Guid createdBy,
        DateTime? startTime,
        DateTime? endTime,
        CancellationToken cancellationToken = default);

    Task<Incident?> GetOpenPreflightIncidentAsync(
        Guid missionId,
        CancellationToken cancellationToken = default);

    Task AddValidationRunAsync(
        MissionValidationRun run,
        CancellationToken cancellationToken = default);

    Task AddIncidentAsync(
        Incident incident,
        CancellationToken cancellationToken = default);

    Task SaveChangesAsync(
        CancellationToken cancellationToken = default);

    Task<T> ExecuteSerializableAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken = default);
}