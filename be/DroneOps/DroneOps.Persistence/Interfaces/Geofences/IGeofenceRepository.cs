using DroneOps.Domain.Entities;

namespace DroneOps.Application.Interfaces.Geofences;

public interface IGeofenceRepository
{
    Task<List<Geofence>> GetAllAsync(
        CancellationToken cancellationToken = default);

    Task<Geofence?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task<bool> NameExistsAsync(
        string name,
        Guid? excludedId = null,
        CancellationToken cancellationToken = default);

    Task AddAsync(
        Geofence geofence,
        CancellationToken cancellationToken = default);

    void Update(Geofence geofence);

    void Delete(Geofence geofence);

    Task SaveChangesAsync(
        CancellationToken cancellationToken = default);
}