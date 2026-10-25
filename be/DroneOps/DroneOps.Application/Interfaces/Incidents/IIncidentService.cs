using DroneOps.Application.DTOs.Request.Incidents;
using DroneOps.Application.DTOs.Response.Incidents;

namespace DroneOps.Application.Interfaces.Incidents;

public interface IIncidentService
{
    Task<PagedIncidentsResponse> GetIncidentsAsync(
        GetIncidentsRequest request,
        Guid? currentUserId,
        bool isManager,
        CancellationToken cancellationToken = default);

    Task<IncidentDetailResponse?> GetIncidentByIdAsync(
        Guid incidentId,
        Guid? currentUserId,
        bool isManager,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lấy sự cố của một nhiệm vụ phục vụ SSE: snapshot ban đầu hoặc các thay đổi sau mốc thời gian.
    /// </summary>
    Task<List<IncidentItemResponse>> GetMissionIncidentFeedAsync(
        Guid missionId,
        Guid? currentUserId,
        bool isManager,
        DateTimeOffset? changedAfter,
        CancellationToken cancellationToken = default);

    Task<IncidentDetailResponse> AcknowledgeAsync(
        Guid incidentId, Guid currentUserId, bool isManager, string? notes,
        CancellationToken cancellationToken = default);

    Task<IncidentDetailResponse> ResolveAsync(
        Guid incidentId, Guid currentUserId, bool isManager, string resolutionNotes,
        CancellationToken cancellationToken = default);

    Task<List<IncidentTransitionResponse>> GetHistoryAsync(
        Guid incidentId, Guid? currentUserId, bool isManager,
        CancellationToken cancellationToken = default);

    Task<List<IncidentAlertResponse>> GetUnreadAlertsAsync(
        Guid currentUserId, CancellationToken cancellationToken = default);

    Task MarkAlertReadAsync(
        Guid alertId, Guid currentUserId, CancellationToken cancellationToken = default);
}
