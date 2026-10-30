using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DroneOps.Application.DTOs.Response.Missions;
using DroneOps.Application.Interfaces.Missions;
using DroneOps.Application.Validators.Missions;
using DroneOps.Persistence.Interfaces;
using Microsoft.Extensions.Options;
using DroneOps.Application.DTOs.Options;

namespace DroneOps.Application.Services.Missions;

public sealed class MissionWeatherQueryService(
 IMissionWeatherQueryRepository repository,
    IMissionWeatherService weather,
    IOptions<MissionWeatherOptions> policy)
    : IMissionWeatherQueryService
{
    public async Task<WeatherAdvisory> ReadAsync(
        Guid id,
        Guid actor,
        bool manager,
        bool refresh,
        CancellationToken ct)
    {
        // =========================
        // GET MISSION
        // =========================

        var mission =
            await repository.GetMissionWithWaypointsAsync(
                id,
                ct)
            ?? throw new KeyNotFoundException(
                "Mission not found.");

        // =========================
        // PERMISSION
        // =========================

        if (!manager &&
            mission.CreatedBy != actor)
        {
            throw new UnauthorizedAccessException(
                "Mission is outside your scope.");
        }

        // =========================
        // CACHE KEY
        // =========================

        var planHash =
            MissionValidationService.Hash(mission);

        var policyJson =
            JsonSerializer.Serialize(
                policy.Value);

        var keySource =
            planHash + policyJson;

        var key =
            Convert.ToHexString(
                SHA256.HashData(
                    Encoding.UTF8.GetBytes(
                        keySource)));

        // =========================
        // READ CACHE
        // =========================

        var cached =
            await repository.GetCacheAsync(
                key,
                ct);

        if (!refresh &&
            cached is not null)
        {
            var result =
                JsonSerializer.Deserialize<WeatherAdvisory>(
                    cached.ForecastJson,
                    MissionValidationService.JsonOptions)
                ?? throw new InvalidOperationException(
                    "Cached weather forecast is invalid.");

            if (cached.ExpiresAt <
                DateTimeOffset.UtcNow)
            {
                result.Status = "Stale";
                result.Available = false;

                result.Warnings.Add(
                    "Cached forecast expired. Refresh to obtain a new forecast.");
            }

            return result;
        }

        // =========================
        // FETCH WEATHER
        // =========================

        WeatherAdvisory value;

        if (weather is MissionWeatherService provider)
        {
            value = await provider.FetchAsync(
                mission,
                refresh,
                ct);
        }
        else
        {
            value = await weather.GetAsync(
                mission,
                ct);
        }

        // =========================
        // ATTACH MISSION INFORMATION
        // =========================

        value.MissionId = id;
        value.PlanHash = planHash;
        value.FlightStartTime =
            mission.StartTime;
        value.FlightEndTime =
            mission.EndTime;

        // =========================
        // PROVIDER FAILED
        // =========================

        if (value.Status == "Unavailable" &&
            cached is not null)
        {
            var old =
                JsonSerializer.Deserialize<WeatherAdvisory>(
                    cached.ForecastJson,
                    MissionValidationService.JsonOptions)
                ?? throw new InvalidOperationException(
                    "Cached weather forecast is invalid.");

            old.Status = "Stale";
            old.Available = false;

            old.Warnings.Add(
                "Provider refresh failed; previous forecast is shown as stale.");

            return old;
        }

        // =========================
        // CACHE EXPIRATION
        // =========================

        if (value.Status == "Unavailable")
        {
            value.ExpiresAt =
                DateTimeOffset.UtcNow
                    .AddMinutes(1);
        }

        var json =
            JsonSerializer.Serialize(
                value,
                MissionValidationService.JsonOptions);

        var expiresAt =
            value.Status == "Unavailable"
                ? DateTimeOffset.UtcNow
                    .AddMinutes(1)
                : value.ExpiresAt;

        // =========================
        // SAVE CACHE
        // =========================

        await repository.UpsertCacheAsync(
            key,
            json,
            value.FetchedAt,
            expiresAt,
            ct);

        return value;
    }
}