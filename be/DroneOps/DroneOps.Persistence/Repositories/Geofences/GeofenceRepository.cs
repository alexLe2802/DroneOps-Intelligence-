using DroneOps.Application.Interfaces.Geofences;
using DroneOps.Domain.Constants;
using DroneOps.Domain.Entities;
using DroneOps.Persistence.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace DroneOps.Persistence.Repositories;

public class GeofenceRepository : IGeofenceRepository
{
    private readonly AppDbContext _context;
    private readonly IMemoryCache _cache;

    public GeofenceRepository(
        AppDbContext context,
        IMemoryCache cache)
    {
        _context = context;
        _cache = cache;
    }

    public async Task<List<Geofence>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        if (_cache.TryGetValue(
                GeofenceCacheKeys.Snapshot,
                out IReadOnlyList<Geofence>? cached) && cached is not null)
        {
            return cached
                .OrderByDescending(x => x.CreatedAt)
                .ToList();
        }

        var snapshot = await _context.Geofences
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        _cache.Set(
            GeofenceCacheKeys.Snapshot,
            (IReadOnlyList<Geofence>)snapshot,
            GeofenceCacheKeys.SnapshotLifetime);
        return snapshot
            .OrderByDescending(x => x.CreatedAt)
            .ToList();
    }

    public Task<Geofence?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        return _context.Geofences
            .FirstOrDefaultAsync(
                x => x.Id == id,
                cancellationToken);
    }

    public Task<bool> NameExistsAsync(
        string name,
        Guid? excludedId = null,
        CancellationToken cancellationToken = default)
    {
        var normalizedName = name.Trim().ToLower();

        return _context.Geofences.AnyAsync(
            x => x.Name.ToLower() == normalizedName
                 && (!excludedId.HasValue ||
                     x.Id != excludedId.Value),
            cancellationToken);
    }

    public async Task AddAsync(
        Geofence geofence,
        CancellationToken cancellationToken = default)
    {
        await _context.Geofences.AddAsync(
            geofence,
            cancellationToken);
    }

    public void Update(Geofence geofence)
    {
        _context.Geofences.Update(geofence);
    }

    public void Delete(Geofence geofence)
    {
        _context.Geofences.Remove(geofence);
    }

    public async Task SaveChangesAsync(
        CancellationToken cancellationToken = default)
    {
        await _context.SaveChangesAsync(cancellationToken);
    }
}
