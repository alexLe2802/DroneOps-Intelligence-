using DroneOps.Application.Interfaces.Geofences;
using DroneOps.Domain.Entities;
using DroneOps.Persistence.Data;
using Microsoft.EntityFrameworkCore;

namespace DroneOps.Persistence.Repositories;

public class GeofenceRepository : IGeofenceRepository
{
    private readonly AppDbContext _context;

    public GeofenceRepository(AppDbContext context)
    {
        _context = context;
    }

    public Task<List<Geofence>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        return _context.Geofences
            .AsNoTracking()
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync(cancellationToken);
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