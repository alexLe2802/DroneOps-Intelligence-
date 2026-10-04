using DroneOps.Application.DTOs.Request.Geofences;
using DroneOps.Application.DTOs.Response.Geofences;

namespace DroneOps.Application.Interfaces.Geofences;

public interface IGeofenceService
{
    Task<List<GeofenceResponse>> GetAllAsync(
        CancellationToken cancellationToken = default);

    Task<GeofenceResponse> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task<GeofenceResponse> CreateAsync(
        CreateGeofenceRequest request,
        CancellationToken cancellationToken = default);

    Task<GeofenceResponse> UpdateAsync(
        Guid id,
        UpdateGeofenceRequest request,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(
        Guid id,
        CancellationToken cancellationToken = default);
}