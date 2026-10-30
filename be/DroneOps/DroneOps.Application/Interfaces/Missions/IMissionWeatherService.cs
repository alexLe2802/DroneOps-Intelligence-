using DroneOps.Application.DTOs.Response.Missions;
using DroneOps.Domain.Entities;

namespace DroneOps.Application.Interfaces.Missions;

public interface IMissionWeatherService
{
    Task<WeatherAdvisory> GetAsync(
        Mission mission,
        CancellationToken cancellationToken = default);
}