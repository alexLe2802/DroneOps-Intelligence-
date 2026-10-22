using DroneOps.Application.DTOs.Request.Users;
using Microsoft.AspNetCore.Http;

namespace DroneOps.Application.Interfaces.Users;

public interface IPilotUpdateRequestService
{
    Task<object> GetAllAsync(
        Guid userId,
        bool isManager,
        PilotUpdateRequestFilterRequest filter,
        CancellationToken cancellationToken = default);

    Task<object?> GetByIdAsync(
        Guid requestId,
        Guid userId,
        bool isManager,
        CancellationToken cancellationToken = default);

    Task<object> SubmitAsync(
    Guid userId,
    PilotUpdateRequestForm form,
    CancellationToken cancellationToken = default);

    Task<object> ApproveAsync(
        Guid requestId,
        Guid reviewedBy,
        CancellationToken cancellationToken = default);

    Task<object> RejectAsync(
        Guid requestId,
        Guid reviewedBy,
        RejectPilotUpdateRequest request,
        CancellationToken cancellationToken = default);
}