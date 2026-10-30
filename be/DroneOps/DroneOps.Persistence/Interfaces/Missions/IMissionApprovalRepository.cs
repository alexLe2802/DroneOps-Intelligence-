using DroneOps.Domain.Entities;

namespace DroneOps.Persistence.Interfaces;

public interface IMissionApprovalRepository
{
    Task<(List<Mission> Missions, int TotalCount)>
        GetPendingApprovalsAsync(
            string? search,
            Guid? uavId,
            DateTime? fromDate,
            DateTime? toDate,
            int pageNumber,
            int pageSize,
            CancellationToken cancellationToken = default);

    Task<Mission?> GetByIdWithDetailsAsync(
        Guid missionId,
        bool tracking = true,
        CancellationToken cancellationToken = default);

    Task<MissionApproval?> GetLatestDecisionAsync(
        Guid missionId,
        Guid managerId,
        string decision,
        CancellationToken cancellationToken = default);

    Task AddApprovalAsync(
        MissionApproval approval,
        CancellationToken cancellationToken = default);

    Task<T> ExecuteSerializableAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken = default);
}