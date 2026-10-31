using System.Text.Json;
using DroneOps.Application.Services;
using DroneOps.Application.Interfaces.Geofences;
using DroneOps.Domain.Entities;

internal static class GeofenceServiceChecks
{
    public static async Task<int> RunAsync()
    {
        var zone = new Geofence
        {
            Id = Guid.NewGuid(), Name = "Object coordinate fixture",
            Coordinates = JsonSerializer.Serialize(new[]
            {
                new { latitude = 10m, longitude = 105m }, new { latitude = 11m, longitude = 105m },
                new { latitude = 11m, longitude = 107m }, new { latitude = 10m, longitude = 107m }
            }),
            MinAltitudeMeters = 50m, MaxAltitudeMeters = 100m
        };
        var service = new GeofenceService(new FixtureRepository(zone));
        var inside = await service.CheckRestrictedAreaAsync(10.5m, 106m, 75m);
        var above = await service.CheckRestrictedAreaAsync(10.5m, 106m, 120m);
        var outside = await service.CheckRestrictedAreaAsync(12m, 106m, 75m);
        Check(inside.IsRestricted && inside.GeofenceId == zone.Id,
            "restricted-area lookup reads canonical latitude/longitude object coordinates");
        Check(!above.IsRestricted, "restricted-area lookup respects the configured altitude band");
        Check(!outside.IsRestricted, "restricted-area lookup does not block an outside coordinate");
        return 3;
    }

    private static void Check(bool condition, string label)
    {
        if (!condition) throw new InvalidOperationException("FAIL: " + label);
        Console.WriteLine("PASS: " + label);
    }

    private sealed class FixtureRepository(Geofence zone) : IGeofenceRepository
    {
        public Task<List<Geofence>> GetAllAsync(CancellationToken cancellationToken = default) => Task.FromResult(new List<Geofence> { zone });
        public Task<Geofence?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult<Geofence?>(zone.Id == id ? zone : null);
        public Task<bool> NameExistsAsync(string name, Guid? excludedId = null, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task AddAsync(Geofence geofence, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Update(Geofence geofence) { }
        public void Delete(Geofence geofence) { }
        public Task SaveChangesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
