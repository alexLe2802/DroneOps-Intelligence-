using DroneOps.Application.DTOs.Response.Missions;

namespace DroneOps.Application.Interfaces.Missions;

public interface IMissionWeatherQueryService
{
    Task<WeatherAdvisory> ReadDraftAsync(DroneOps.Domain.Entities.Mission draft, CancellationToken ct);

    Task<WeatherAdvisory> ReadAsync(
        Guid id,
        Guid actor,
        bool manager,
        bool refresh,
        CancellationToken cancellationToken = default, Guid? updateRequestId = null);
}