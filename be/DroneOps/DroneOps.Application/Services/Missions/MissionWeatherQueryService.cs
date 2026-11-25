using DroneOps.Domain.Entities;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DroneOps.Application.DTOs.Response.Missions;
using DroneOps.Application.DTOs.Request.Missions;
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
        CancellationToken ct, Guid? updateRequestId = null)
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

        if (updateRequestId.HasValue)
        {
            var review = await repository.GetReviewRequestAsync(id, updateRequestId.Value, ct)
                ?? throw new KeyNotFoundException("Review request not found.");
            var dto = JsonSerializer.Deserialize<CreateMissionRequest>(review.ProposedPlanJson, MissionValidationService.JsonOptions)
                ?? throw new InvalidOperationException("Proposed plan is missing.");
            mission = MissionPlans.Propose(mission, dto);
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
            planHash + policyJson + updateRequestId?.ToString();

        var key =
            Convert.ToHexString(
                SHA256.HashData(
                    Encoding.UTF8.GetBytes(
                        keySource)));

        var cached = await repository.GetCacheAsync(key, ct);
        if (Reusable(cached) is { } current)
            return current;

        WeatherAdvisory? fetched = null;
        return await repository.WithRefreshLockAsync(key, async () =>
        {
            // Another request may have populated the cache while this one waited.
            cached = await repository.GetCacheAsync(key, ct);
            if (Reusable(cached) is { } updated)
                return updated;

            // Finish the bounded provider fetch and persist its cooldown even if the browser disconnects.
            fetched ??= weather is MissionWeatherService provider
                ? await provider.FetchAsync(mission, refresh, CancellationToken.None)
                : await weather.GetAsync(mission, CancellationToken.None);
            var value = fetched;
            value.MissionId = id;
            value.UpdateRequestId = updateRequestId;
            value.PlanHash = planHash;
            value.FlightStartTime = mission.StartTime;
            value.FlightEndTime = mission.EndTime;

            var retryAt = DateTimeOffset.UtcNow.AddSeconds(60);
            if (value.Status == "Unavailable")
            {
                value.ExpiresAt = retryAt;
                if (cached is not null && Decode(cached) is { Status: not "Unavailable" } previous)
                {
                    const string warning = "Provider refresh failed; previous forecast is shown as stale.";
                    previous.Status = "Stale";
                    previous.Available = false;
                    if (!previous.Warnings.Contains(warning))
                        previous.Warnings.Add(warning);
                    value = previous;
                }
            }

            var nextRefresh = value.Status is "Unavailable" or "Stale" || value.ExpiresAt < retryAt
                ? retryAt : value.ExpiresAt;
            value.NextRefreshAt = nextRefresh;
            await repository.UpsertCacheAsync(key,
                JsonSerializer.Serialize(value, MissionValidationService.JsonOptions),
                value.FetchedAt, nextRefresh, CancellationToken.None);
            return value;
        }, ct);
    }

    private static WeatherAdvisory? Reusable(MissionWeatherCache? cached)
    {
        if (cached is null || cached.ExpiresAt <= DateTimeOffset.UtcNow) return null;
        var value = Decode(cached);
        value.NextRefreshAt = cached.ExpiresAt;
        return value;
    }

    private static WeatherAdvisory Decode(MissionWeatherCache cached) =>
        JsonSerializer.Deserialize<WeatherAdvisory>(cached.ForecastJson, MissionValidationService.JsonOptions)
        ?? throw new InvalidOperationException("Cached weather forecast is invalid.");
}
