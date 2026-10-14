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
}