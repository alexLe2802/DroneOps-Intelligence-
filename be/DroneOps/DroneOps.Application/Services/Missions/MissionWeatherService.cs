using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using DroneOps.Application.DTOs.Options;
using DroneOps.Application.DTOs.Response.Missions;
using DroneOps.Application.Interfaces.Missions;
using DroneOps.Application.Validators.Missions;
using DroneOps.Domain.Entities;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace DroneOps.Application.Services.Missions;

public sealed class MissionWeatherService(
    HttpClient http,
    IOptions<MissionWeatherOptions> options,
    IMemoryCache cache)
    : IMissionWeatherService
{
    private sealed record CachedHours(
        JsonElement Hours,
        DateTimeOffset FetchedAt);

    private static readonly string[] Variables =
    [
        "temperature_2m",
        "relative_humidity_2m",
        "wind_speed_10m",
        "wind_direction_10m",
        "wind_gusts_10m",
        "precipitation_probability",
        "rain",
        "visibility",
        "cloud_cover",
        "weather_code"
    ];

    public Task<WeatherAdvisory> GetAsync(
        Mission mission,
        CancellationToken cancellationToken = default)
    {
        return FetchAsync(
            mission,
            false,
            cancellationToken);
    }

    public async Task<WeatherAdvisory> FetchAsync(
        Mission mission,
        bool force,
        CancellationToken cancellationToken = default)
    {
        var result = new WeatherAdvisory
        {
            MissionId = mission.Id,
            PlanHash = MissionValidationService.Hash(mission),
            FlightStartTime = mission.StartTime,
            FlightEndTime = mission.EndTime
        };

        using var budget =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);

        budget.CancelAfter(
            TimeSpan.FromSeconds(12));

        var points = mission.Waypoints
            .OrderBy(x => x.SequenceOrder)
            .ToList();

        var fetched =
            new Dictionary<
                string,
                (CachedHours? Data, bool Stale)>();

        foreach (var point in points)
        {
           
            var latitude =
                Math.Round(point.Latitude, 2);

            var longitude =
                Math.Round(point.Longitude, 2);

            var latitudeText =
                latitude.ToString(
                    "F2",
                    CultureInfo.InvariantCulture);

            var longitudeText =
                longitude.ToString(
                    "F2",
                    CultureInfo.InvariantCulture);

            var cacheKey =
                $"open-meteo-v2:{latitudeText}:{longitudeText}";

            if (!fetched.TryGetValue(
                    cacheKey,
                    out var raw))
            {
                cache.TryGetValue<CachedHours>(
                    cacheKey,
                    out var stored);

                raw = (stored, false);

                var cacheExpired =
                    stored is null ||
                    stored.FetchedAt <
                    DateTimeOffset.UtcNow.AddMinutes(-10);

                if (force || cacheExpired)
                {
                    try
                    {
                        var hourlyVariables =
                            string.Join(
                                ",",
                                Variables);

                        var uri =
                            $"https://api.open-meteo.com/v1/forecast" +
                            $"?latitude={latitudeText}" +
                            $"&longitude={longitudeText}" +
                            $"&hourly={hourlyVariables}" +
                            $"&wind_speed_unit=ms" +
                            $"&timezone=GMT" +
                            $"&forecast_days=16";

                        using var document =
                            await http.GetFromJsonAsync<JsonDocument>(
                                uri,
                                budget.Token)
                            ?? throw new JsonException(
                                "Weather provider returned an empty response.");

                        var hours = document.RootElement
                            .GetProperty("hourly")
                            .Clone();

                        if (!hours.TryGetProperty(
                                "time",
                                out var times) ||
                            times.ValueKind !=
                            JsonValueKind.Array)
                        {
                            throw new JsonException(
                                "Weather provider response does not contain hourly time data.");
                        }

                        var newCache = new CachedHours(
                            hours,
                            DateTimeOffset.UtcNow);

                        raw = (
                            newCache,
                            false);

                        cache.Set(
                            cacheKey,
                            newCache,
                            TimeSpan.FromHours(24));
                    }
                    catch (Exception)
                        when (!cancellationToken
                            .IsCancellationRequested)
                    {
                       
                        raw = (
                            stored,
                            stored is not null);
                    }
                }

                fetched[cacheKey] = raw;
            }

            var waypointWeather =
                new WaypointWeather
                {
                    SequenceOrder =
                        point.SequenceOrder,

                    Latitude =
                        point.Latitude,

                    Longitude =
                        point.Longitude,

                    AltitudeMeters =
                        point.Altitude,

                    EstimatedArrivalTime =
                        null,

                    ArrivalTimeReason =
                        "Forecast based on the scheduled mission flight window.",

                    SourceFetchedAt =
                        raw.Data?.FetchedAt
                };

            if (raw.Data is not null &&
                mission.StartTime.HasValue &&
                mission.EndTime.HasValue)
            {
                MapForecastToWaypoint(
                    waypointWeather,
                    raw.Data,
                    raw.Stale,
                    mission.StartTime.Value,
                    mission.EndTime.Value);
            }

            if (waypointWeather.Status == "Unavailable")
            {
                waypointWeather.Warnings.Add(
                    $"WP{point.SequenceOrder}: forecast unavailable " +
                    "(provider error, missing data or outside forecast horizon).");
            }

            waypointWeather.RiskLevel =
                waypointWeather.WeatherCode is
                    95 or 96 or 99
                    ? "High"
                    : waypointWeather.Warnings.Count > 0
                        ? "Review"
                        : "Low";

            result.Waypoints.Add(
                waypointWeather);

            result.Warnings.AddRange(
                waypointWeather.Warnings);
        }

        result.Status =
            CalculateOverallStatus(
                points.Count,
                result.Waypoints);

        result.Available =
            result.Status == "Available";

        result.OverallRisk =
            CalculateOverallRisk(result);

        result.FetchedAt =
            result.Waypoints
                .Where(x =>
                    x.SourceFetchedAt.HasValue)
                .Select(x =>
                    x.SourceFetchedAt!.Value)
                .DefaultIfEmpty(
                    DateTimeOffset.UtcNow)
                .Min();

        result.ExpiresAt =
            result.FetchedAt.AddMinutes(10);

        result.MaxWindMetersPerSecond =
            result.Waypoints
                .Select(x =>
                    x.WindSpeedMetersPerSecond)
                .Where(x => x.HasValue)
                .Select(x => x!.Value)
                .DefaultIfEmpty()
                .Max();

        result.MaxGustMetersPerSecond =
            result.Waypoints
                .Select(x =>
                    x.WindGustMetersPerSecond)
                .Where(x => x.HasValue)
                .Select(x => x!.Value)
                .DefaultIfEmpty()
                .Max();

        result.MaxRainMillimetersPerHour =
            result.Waypoints
                .Select(x =>
                    x.PrecipitationMillimeters)
                .Where(x => x.HasValue)
                .Select(x => x!.Value)
                .DefaultIfEmpty()
                .Max();

        result.ThunderstormForecast =
            result.Waypoints.Any(x =>
                x.WeatherCode is
                    95 or 96 or 99);

        return result;
    }

    private void MapForecastToWaypoint(
        WaypointWeather waypointWeather,
        CachedHours cachedHours,
        bool stale,
        DateTime flightStart,
        DateTime flightEnd)
    {
        var hours = cachedHours.Hours;

        if (!hours.TryGetProperty(
                "time",
                out var times) ||
            times.ValueKind !=
            JsonValueKind.Array)
        {
            return;
        }

        var selectedIndex = -1;
        var coveredIndexes = new List<int>();

        for (var index = 0;
             index < times.GetArrayLength();
             index++)
        {
            var timeText =
                times[index].GetString();

            if (!DateTime.TryParse(
                    timeText,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal |
                    DateTimeStyles.AdjustToUniversal,
                    out var forecastTime))
            {
                continue;
            }

            var overlapsFlightWindow =
                forecastTime < flightEnd &&
                forecastTime.AddHours(1) >
                flightStart;

            if (!overlapsFlightWindow)
            {
                continue;
            }

            coveredIndexes.Add(index);

            if (selectedIndex < 0)
            {
                selectedIndex = index;
            }
        }

        if (selectedIndex < 0 ||
            coveredIndexes.Count == 0)
        {
            return;
        }

        decimal? Metric(
            string name,
            int index)
        {
            if (!hours.TryGetProperty(
                    name,
                    out var values) ||
                values.ValueKind !=
                JsonValueKind.Array ||
                index >= values.GetArrayLength())
            {
                return null;
            }

            var value = values[index];

            if (value.ValueKind !=
                JsonValueKind.Number)
            {
                return null;
            }

            return value.TryGetDecimal(
                out var number)
                ? number
                : null;
        }

        var selectedTimeText =
            times[selectedIndex].GetString();

        if (DateTime.TryParse(
                selectedTimeText,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal |
                DateTimeStyles.AdjustToUniversal,
                out var selectedForecastTime))
        {
            waypointWeather.ForecastTime =
                selectedForecastTime;
        }

        waypointWeather.TemperatureCelsius =
            Metric(
                "temperature_2m",
                selectedIndex);

        waypointWeather.RelativeHumidityPercent =
            Metric(
                "relative_humidity_2m",
                selectedIndex);

        waypointWeather.WindDirectionDegrees =
            Metric(
                "wind_direction_10m",
                selectedIndex);

        waypointWeather.CloudCoverPercent =
            Metric(
                "cloud_cover",
                selectedIndex);

        waypointWeather.PrecipitationProbabilityPercent =
            MaximumMetric(
                coveredIndexes,
                index => Metric(
                    "precipitation_probability",
                    index));

        waypointWeather.VisibilityMeters =
            MinimumMetric(
                coveredIndexes,
                index => Metric(
                    "visibility",
                    index));

        waypointWeather.WindSpeedMetersPerSecond =
            MaximumMetric(
                coveredIndexes,
                index => Metric(
                    "wind_speed_10m",
                    index));

        waypointWeather.WindGustMetersPerSecond =
            MaximumMetric(
                coveredIndexes,
                index => Metric(
                    "wind_gusts_10m",
                    index));

        waypointWeather.PrecipitationMillimeters =
            MaximumMetric(
                coveredIndexes,
                index => Metric(
                    "rain",
                    index));

        var thunderstormCode =
            coveredIndexes
                .Select(index =>
                    (int?)Metric(
                        "weather_code",
                        index))
                .FirstOrDefault(code =>
                    code is 95 or 96 or 99);

        waypointWeather.WeatherCode =
            thunderstormCode ??
            (int?)Metric(
                "weather_code",
                selectedIndex);

        var coveredTimes =
            coveredIndexes
                .Select(index =>
                {
                    var timeText =
                        times[index].GetString();

                    return DateTime.Parse(
                        timeText!,
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal |
                        DateTimeStyles.AdjustToUniversal);
                })
                .Order()
                .ToList();

        var allMetricsAvailable =
            coveredIndexes.All(index =>
                Variables.All(variable =>
                    Metric(
                        variable,
                        index)
                    .HasValue));

        var timeSeriesContinuous =
            coveredTimes
                .Zip(coveredTimes.Skip(1))
                .All(pair =>
                    pair.Second -
                    pair.First <=
                    TimeSpan.FromHours(1));

        var complete =
            allMetricsAvailable &&
            timeSeriesContinuous;

        var finalCoveredTime =
            DateTime.Parse(
                    times[coveredIndexes[^1]]
                        .GetString()!,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal |
                    DateTimeStyles.AdjustToUniversal)
                .AddHours(1);

        if (stale)
        {
            waypointWeather.Status = "Stale";
        }
        else if (
            complete &&
            waypointWeather.ForecastTime <=
            flightStart &&
            finalCoveredTime >= flightEnd)
        {
            waypointWeather.Status = "Available";
        }
        else
        {
            waypointWeather.Status = "Partial";
        }

        if (waypointWeather.Status != "Available")
        {
            waypointWeather.Warnings.Add(
                $"WP{waypointWeather.SequenceOrder}: " +
                $"forecast is " +
                $"{waypointWeather.Status.ToLowerInvariant()} " +
                "for the requested flight window.");
        }

        AddWarnings(
            waypointWeather,
            options.Value);
    }

    private static decimal? MaximumMetric(
        IEnumerable<int> indexes,
        Func<int, decimal?> selector)
    {
        var values = indexes
            .Select(selector)
            .Where(x => x.HasValue)
            .Select(x => x!.Value)
            .ToList();

        return values.Count == 0
            ? null
            : values.Max();
    }

    private static decimal? MinimumMetric(
        IEnumerable<int> indexes,
        Func<int, decimal?> selector)
    {
        var values = indexes
            .Select(selector)
            .Where(x => x.HasValue)
            .Select(x => x!.Value)
            .ToList();

        return values.Count == 0
            ? null
            : values.Min();
    }

    private static string CalculateOverallStatus(
        int waypointCount,
        List<WaypointWeather> waypointWeather)
    {
        if (waypointCount == 0 ||
            waypointWeather.All(x =>
                x.Status == "Unavailable"))
        {
            return "Unavailable";
        }

        if (waypointWeather.Any(x =>
                x.Status == "Stale"))
        {
            return "Stale";
        }

        if (waypointWeather.All(x =>
                x.Status == "Available"))
        {
            return "Available";
        }

        return "Partial";
    }

    private static string CalculateOverallRisk(
        WeatherAdvisory result)
    {
        if (result.Status == "Unavailable")
        {
            return "Unknown";
        }

        if (result.Waypoints.Any(x =>
                x.RiskLevel == "High"))
        {
            return "High";
        }

        return result.Warnings.Count > 0
            ? "Review"
            : "Low";
    }

    private static void AddWarnings(
        WaypointWeather waypointWeather,
        MissionWeatherOptions policy)
    {
        void AddThresholdWarning(
            string name,
            decimal? value,
            decimal? threshold,
            string unit)
        {
            if (!value.HasValue ||
                !threshold.HasValue ||
                value < threshold)
            {
                return;
            }

            waypointWeather.Warnings.Add(
                $"WP{waypointWeather.SequenceOrder}: " +
                $"{name} {value} {unit} >= configured " +
                $"advisory threshold {threshold} {unit}.");
        }

        AddThresholdWarning(
            "wind",
            waypointWeather.WindSpeedMetersPerSecond,
            policy.WindWarningMetersPerSecond,
            "m/s");

        AddThresholdWarning(
            "gust",
            waypointWeather.WindGustMetersPerSecond,
            policy.GustWarningMetersPerSecond,
            "m/s");

        if (waypointWeather.PrecipitationMillimeters > 0)
        {
            AddThresholdWarning(
                "rain",
                waypointWeather.PrecipitationMillimeters,
                policy.RainWarningMillimetersPerHour ?? 0,
                "mm/hour");
        }

        if (waypointWeather.WeatherCode is
            95 or 96 or 99)
        {
            waypointWeather.Warnings.Add(
                $"WP{waypointWeather.SequenceOrder}: " +
                $"thunderstorm forecast " +
                $"(WMO {waypointWeather.WeatherCode}).");
        }

        if (!policy.WindWarningMetersPerSecond.HasValue ||
            !policy.GustWarningMetersPerSecond.HasValue)
        {
            waypointWeather.Warnings.Add(
                $"WP{waypointWeather.SequenceOrder}: " +
                "wind/gust advisory thresholds not configured; " +
                "review reported values manually.");
        }
    }
}