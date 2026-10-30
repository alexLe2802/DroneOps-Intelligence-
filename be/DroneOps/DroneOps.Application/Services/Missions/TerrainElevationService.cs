namespace DroneOps.Application.Services.Missions;

public sealed record TerrainElevation(decimal? ElevationMslMeters, string Source)
{
    public bool Available => ElevationMslMeters.HasValue;
}

public interface ITerrainElevationService
{
    Task<TerrainElevation> GetElevationAsync(decimal latitude, decimal longitude, CancellationToken cancellationToken);
}

/// <summary>
/// Fail-closed default. Deployments must register a measured terrain provider before
/// validation may convert between AGL and MSL.
/// </summary>
public sealed class UnavailableTerrainElevationService : ITerrainElevationService
{
    public Task<TerrainElevation> GetElevationAsync(decimal latitude, decimal longitude, CancellationToken cancellationToken) =>
        Task.FromResult(new TerrainElevation(null, "Unavailable"));
}
