using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DroneOps.Application.DTOs.Options;
using DroneOps.Application.DTOs.Response.Missions;
using DroneOps.Application.Interfaces.Missions;
using DroneOps.Application.Validators.Missions;
using DroneOps.Domain.Entities;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DroneOps.Application.Services.Missions;

public sealed class MissionWeatherService(
    HttpClient http,
    IOptions<MissionWeatherOptions> options,
    IMemoryCache cache,
    ILogger<MissionWeatherService> logger)
    : IMissionWeatherService
{
    private const string ProviderUrl =
        "https://api.open-meteo.com/v1/forecast";

    private const string FallbackProviderUrl =
        "https://api.met.no/weatherapi/locationforecast/2.0/compact";

    private const string FallbackUserAgent =
        "DroneOpsIntelligence/1.0 (+https://droneops.click)";

    private sealed record CachedHours(
        JsonElement Hours,
        DateTimeOffset FetchedAt,
        string Provider = "Open-Meteo",
        int MaxSlotHours = 1);

    private sealed record RoutePoint(
        int SequenceOrder,
        bool IsRouteSample,
        int SampleOrder,
        int? SegmentFrom,
        int? SegmentTo,
        decimal Latitude,
        decimal Longitude,
        decimal? Altitude,
        decimal? BearingDegrees,
        double DistanceFromStartMeters);

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
        var policy = options.Value;

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
            TimeSpan.FromSeconds(
                Math.Max(5, policy.RequestBudgetSeconds)));

        var orderedWaypoints = MissionRouteGeometry.PlanningWaypoints(mission);

        var routePoints =
            BuildRoutePoints(
                orderedWaypoints,
                policy);

        var totalDistance =
            routePoints.Count == 0
                ? 0
                : routePoints.Max(x =>
                    x.DistanceFromStartMeters);

        var fetched =
            new Dictionary<
                string,
                (CachedHours? Data, bool Stale)>();

        var providersUsed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var routePoint in routePoints)
        {
            var raw = await GetForecastAsync(
                routePoint,
                fetched,
                force,
                policy,
                budget.Token,
                cancellationToken);

            if (raw.Data is not null)
            {
                providersUsed.Add(raw.Data.Provider);
            }

            var estimatedArrival =
                EstimateArrivalTime(
                    mission,
                    routePoint.DistanceFromStartMeters,
                    totalDistance);

            var weatherPoint = new WaypointWeather
            {
                SequenceOrder =
                    routePoint.SequenceOrder,

                IsRouteSample =
                    routePoint.IsRouteSample,

                SampleOrder =
                    routePoint.SampleOrder,

                SegmentFrom =
                    routePoint.SegmentFrom,

                SegmentTo =
                    routePoint.SegmentTo,

                SegmentBearingDegrees =
                    routePoint.BearingDegrees,

                Latitude =
                    routePoint.Latitude,

                Longitude =
                    routePoint.Longitude,

                AltitudeMeters =
                    routePoint.Altitude,

                EstimatedArrivalTime =
                    estimatedArrival,

                ArrivalTimeReason =
                    estimatedArrival.HasValue
                        ? "Estimated from route distance and the scheduled mission flight window."
                        : "Flight window or route distance is unavailable.",

                SourceFetchedAt =
                    raw.Data?.FetchedAt
            };

            if (raw.Data is not null &&
                mission.StartTime.HasValue &&
                mission.EndTime.HasValue)
            {
                MapForecastToPoint(
                    weatherPoint,
                    raw.Data,
                    raw.Stale,
                    mission.StartTime.Value,
                    mission.EndTime.Value);
            }

            CalculateWindComponents(
                weatherPoint);

            CalculateRisk(
                weatherPoint,
                policy);

            if (weatherPoint.Status == "Unavailable")
            {
                var warning =
                    $"{PointLabel(weatherPoint)}: forecast unavailable " +
                    "(provider error, missing data or outside forecast horizon).";

                weatherPoint.Warnings.Add(warning);

                weatherPoint.RiskLevel = "Unknown";
                weatherPoint.RiskScore = 0;
            }

            result.Waypoints.Add(
                weatherPoint);

            result.Warnings.AddRange(
                weatherPoint.Warnings);
        }

        result.Status =
            CalculateOverallStatus(
                routePoints.Count,
                result.Waypoints);

        if (providersUsed.Count > 0)
        {
            result.Provider = string.Join(" + ", providersUsed);
        }

        result.Available =
            result.Status == "Available";

        result.OverallRisk =
            CalculateOverallRisk(result);

        result.OverallRiskScore =
            result.Waypoints
                .Where(x =>
                    x.RiskLevel != "Unknown")
                .Select(x =>
                    x.RiskScore)
                .DefaultIfEmpty(0)
                .Max();

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
            result.FetchedAt.AddMinutes(
                Math.Max(1, policy.CacheMinutes));

        result.MaxWindMetersPerSecond =
            Maximum(
                result.Waypoints.Select(x =>
                    x.WindSpeedMetersPerSecond));

        result.MaxGustMetersPerSecond =
            Maximum(
                result.Waypoints.Select(x =>
                    x.WindGustMetersPerSecond));

        result.MaxCrosswindMetersPerSecond =
            Maximum(
                result.Waypoints.Select(x =>
                    x.CrosswindMetersPerSecond));

        result.MaxRainMillimetersPerHour =
            Maximum(
                result.Waypoints.Select(x =>
                    x.PrecipitationMillimeters));

        result.ThunderstormForecast =
            result.Waypoints.Any(x =>
                x.WeatherCode is
                    95 or 96 or 99);

        return result;
    }

    // =========================================================
    // ROUTE SAMPLING
    // =========================================================

    private static List<RoutePoint> BuildRoutePoints(
        List<Waypoint> waypoints,
        MissionWeatherOptions policy)
    {
        var result = new List<RoutePoint>();

        if (waypoints.Count == 0)
        {
            return result;
        }

        if (waypoints.Count == 1)
        {
            var point = waypoints[0];

            result.Add(
                new RoutePoint(
                    point.SequenceOrder,
                    false,
                    0,
                    null,
                    null,
                    point.Latitude,
                    point.Longitude,
                    point.Altitude,
                    null,
                    0));

            return result;
        }

        var segments =
            new List<(
                Waypoint From,
                Waypoint To,
                double Distance,
                decimal Bearing)>();

        var totalDistance = 0d;

        for (var index = 1;
             index < waypoints.Count;
             index++)
        {
            var from = waypoints[index - 1];
            var to = waypoints[index];

            var distance =
                HaversineDistanceMeters(
                    from.Latitude,
                    from.Longitude,
                    to.Latitude,
                    to.Longitude);

            var bearing =
                CalculateBearingDegrees(
                    from.Latitude,
                    from.Longitude,
                    to.Latitude,
                    to.Longitude);

            segments.Add(
                (from, to, distance, bearing));

            totalDistance += distance;
        }

        var configuredSpacing =
            Math.Max(
                100d,
                (double)policy.RouteSampleDistanceMeters);

        var maxSamples =
            Math.Max(
                1,
                policy.MaxRouteSamples);

        /*
         * Nếu tuyến quá dài, tự tăng khoảng cách sample để toàn bộ
         * route vẫn được phủ mà không tạo quá nhiều HTTP locations.
         */
        var adaptiveSpacing =
            Math.Max(
                configuredSpacing,
                totalDistance / maxSamples);

        var cumulativeDistance = 0d;
        var sampleOrder = 1;

        foreach (var segment in segments)
        {
            result.Add(
                new RoutePoint(
                    segment.From.SequenceOrder,
                    false,
                    0,
                    segment.From.SequenceOrder,
                    segment.To.SequenceOrder,
                    segment.From.Latitude,
                    segment.From.Longitude,
                    segment.From.Altitude,
                    segment.Bearing,
                    cumulativeDistance));

            var sampleCount =
                (int)Math.Floor(
                    segment.Distance /
                    adaptiveSpacing);

            for (var sampleIndex = 1;
                 sampleIndex <= sampleCount;
                 sampleIndex++)
            {
                var distanceOnSegment =
                    sampleIndex *
                    adaptiveSpacing;

                if (distanceOnSegment >=
                    segment.Distance)
                {
                    break;
                }

                var fraction =
                    distanceOnSegment /
                    segment.Distance;

                var latitude =
                    Interpolate(
                        segment.From.Latitude,
                        segment.To.Latitude,
                        fraction);

                var longitude =
                    InterpolateLongitude(
                        segment.From.Longitude,
                        segment.To.Longitude,
                        fraction);

                decimal? altitude = null;

                if (segment.From.Altitude.HasValue &&
                    segment.To.Altitude.HasValue)
                {
                    altitude = Interpolate(
                        segment.From.Altitude.Value,
                        segment.To.Altitude.Value,
                        fraction);
                }

                result.Add(
                    new RoutePoint(
                        segment.From.SequenceOrder,
                        true,
                        sampleOrder++,
                        segment.From.SequenceOrder,
                        segment.To.SequenceOrder,
                        latitude,
                        longitude,
                        altitude,
                        segment.Bearing,
                        cumulativeDistance +
                        distanceOnSegment));
            }

            cumulativeDistance +=
                segment.Distance;
        }

        var lastSegment = segments[^1];
        var lastWaypoint = waypoints[^1];

        result.Add(
            new RoutePoint(
                lastWaypoint.SequenceOrder,
                false,
                0,
                lastSegment.From.SequenceOrder,
                lastSegment.To.SequenceOrder,
                lastWaypoint.Latitude,
                lastWaypoint.Longitude,
                lastWaypoint.Altitude,
                lastSegment.Bearing,
                cumulativeDistance));

        return result
            .OrderBy(x =>
                x.DistanceFromStartMeters)
            .ToList();
    }

    private static decimal Interpolate(
        decimal from,
        decimal to,
        double fraction)
    {
        return from +
               (to - from) *
               (decimal)fraction;
    }

    private static decimal InterpolateLongitude(
        decimal from,
        decimal to,
        double fraction)
    {
        var difference = to - from;

        if (difference > 180)
        {
            difference -= 360;
        }
        else if (difference < -180)
        {
            difference += 360;
        }

        var value =
            from +
            difference *
            (decimal)fraction;

        if (value > 180)
        {
            value -= 360;
        }
        else if (value < -180)
        {
            value += 360;
        }

        return value;
    }

    // =========================================================
    // FORECAST PROVIDER / CACHE
    // =========================================================

    private async Task<(CachedHours? Data, bool Stale)>
        GetForecastAsync(
            RoutePoint routePoint,
            Dictionary<
                string,
                (CachedHours? Data, bool Stale)> fetched,
            bool force,
            MissionWeatherOptions policy,
            CancellationToken budgetToken,
            CancellationToken cancellationToken)
    {
        var latitude =
            Math.Round(routePoint.Latitude, 2);

        var longitude =
            Math.Round(routePoint.Longitude, 2);

        var latitudeText =
            latitude.ToString(
                "F2",
                CultureInfo.InvariantCulture);

        var longitudeText =
            longitude.ToString(
                "F2",
                CultureInfo.InvariantCulture);

        var cacheKey =
            $"open-meteo-v3:{latitudeText}:{longitudeText}";

        if (fetched.TryGetValue(
                cacheKey,
                out var existing))
        {
            return existing;
        }

        cache.TryGetValue<CachedHours>(
            cacheKey,
            out var stored);

        var result =
            (Data: stored, Stale: false);

        var cacheExpired =
            stored is null ||
            stored.FetchedAt <
            DateTimeOffset.UtcNow.AddMinutes(
                -Math.Max(1, policy.CacheMinutes));

        if (force || cacheExpired)
        {
            try
            {
                var hourlyVariables =
                    string.Join(
                        ",",
                        Variables);

                var forecastDays =
                    Math.Clamp(
                        policy.ForecastDays,
                        1,
                        16);

                var uri =
                    $"{ProviderUrl}" +
                    $"?latitude={latitudeText}" +
                    $"&longitude={longitudeText}" +
                    $"&hourly={hourlyVariables}" +
                    $"&wind_speed_unit=ms" +
                    $"&timezone=GMT" +
                    $"&forecast_days={forecastDays}";

                using var response = await http.GetAsync(
                    uri, HttpCompletionOption.ResponseHeadersRead, budgetToken);

                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    await LogRateLimitAsync(response, budgetToken);
                }

                response.EnsureSuccessStatusCode();
                using var document =
                    await response.Content.ReadFromJsonAsync<JsonDocument>(budgetToken)
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

                var newCache =
                    new CachedHours(
                        hours,
                        DateTimeOffset.UtcNow,
                        "Open-Meteo",
                        1);

                result =
                    (newCache, false);

                cache.Set(
                    cacheKey,
                    newCache,
                    TimeSpan.FromHours(24));
            }
            catch (Exception ex)
                when (!cancellationToken
                    .IsCancellationRequested)
            {
                logger.LogWarning(
                    ex,
                    "Open-Meteo forecast failed for ({Latitude}, {Longitude}); attempting MET Norway fallback.",
                    latitudeText,
                    longitudeText);

                var fallback = await TryFetchMetNorwayAsync(
                    latitudeText,
                    longitudeText,
                    budgetToken,
                    cancellationToken);

                if (fallback is not null)
                {
                    result = (fallback, false);
                    cache.Set(
                        cacheKey,
                        fallback,
                        TimeSpan.FromHours(6));
                }
                else
                {
                    result =
                        (stored, stored is not null);
                }
            }
        }

        fetched[cacheKey] = result;

        return result;
    }

    private async Task<CachedHours?> TryFetchMetNorwayAsync(
        string latitudeText,
        string longitudeText,
        CancellationToken budgetToken,
        CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"{FallbackProviderUrl}?lat={latitudeText}&lon={longitudeText}");

            request.Headers.TryAddWithoutValidation("User-Agent", FallbackUserAgent);
            request.Headers.TryAddWithoutValidation("Accept", "application/json");

            using var response = await http.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                budgetToken);

            response.EnsureSuccessStatusCode();

            using var document =
                await response.Content.ReadFromJsonAsync<JsonDocument>(budgetToken);

            if (document is null ||
                !document.RootElement.TryGetProperty("properties", out var props) ||
                !props.TryGetProperty("timeseries", out var series) ||
                series.ValueKind != JsonValueKind.Array ||
                series.GetArrayLength() == 0)
            {
                return null;
            }

            var times = new List<string>();
            var temps = new List<decimal?>();
            var humidities = new List<decimal?>();
            var windSpeeds = new List<decimal?>();
            var windDirs = new List<decimal?>();
            var windGusts = new List<decimal?>();
            var rainProbs = new List<decimal?>();
            var rains = new List<decimal?>();
            var visibilities = new List<decimal?>();
            var clouds = new List<decimal?>();
            var codes = new List<int?>();

            foreach (var entry in series.EnumerateArray())
            {
                if (!entry.TryGetProperty("time", out var timeEl) ||
                    timeEl.GetString() is not { Length: > 0 } timeText ||
                    !entry.TryGetProperty("data", out var dataEl) ||
                    !dataEl.TryGetProperty("instant", out var instantEl) ||
                    !instantEl.TryGetProperty("details", out var detailsEl))
                {
                    continue;
                }

                decimal? ReadNum(JsonElement parent, string name) =>
                    parent.TryGetProperty(name, out var v) &&
                    v.ValueKind == JsonValueKind.Number &&
                    v.TryGetDecimal(out var d)
                        ? d
                        : null;

                decimal? rainMm = null;
                string? symbol = null;

                if (dataEl.TryGetProperty("next_1_hours", out var next1))
                {
                    if (next1.TryGetProperty("details", out var d1))
                        rainMm = ReadNum(d1, "precipitation_amount");
                    if (next1.TryGetProperty("summary", out var s1) &&
                        s1.TryGetProperty("symbol_code", out var sym1))
                        symbol = sym1.GetString();
                }
                else if (dataEl.TryGetProperty("next_6_hours", out var next6))
                {
                    if (next6.TryGetProperty("details", out var d6))
                    {
                        var sixHourRain = ReadNum(d6, "precipitation_amount");
                        rainMm = sixHourRain.HasValue
                            ? Math.Round(sixHourRain.Value / 6m, 2)
                            : null;
                    }
                    if (next6.TryGetProperty("summary", out var s6) &&
                        s6.TryGetProperty("symbol_code", out var sym6))
                        symbol = sym6.GetString();
                }

                times.Add(timeText);
                temps.Add(ReadNum(detailsEl, "air_temperature"));
                humidities.Add(ReadNum(detailsEl, "relative_humidity"));
                windSpeeds.Add(ReadNum(detailsEl, "wind_speed"));
                windDirs.Add(ReadNum(detailsEl, "wind_from_direction"));
                windGusts.Add(ReadNum(detailsEl, "wind_speed_of_gust"));
                rainProbs.Add(null);
                rains.Add(rainMm);
                visibilities.Add(null);
                clouds.Add(ReadNum(detailsEl, "cloud_area_fraction"));
                codes.Add(MapMetNorwaySymbolToWmo(symbol));
            }

            if (times.Count == 0)
            {
                return null;
            }

            var payload = new Dictionary<string, object?>
            {
                ["time"] = times,
                ["temperature_2m"] = temps,
                ["relative_humidity_2m"] = humidities,
                ["wind_speed_10m"] = windSpeeds,
                ["wind_direction_10m"] = windDirs,
                ["wind_gusts_10m"] = windGusts,
                ["precipitation_probability"] = rainProbs,
                ["rain"] = rains,
                ["visibility"] = visibilities,
                ["cloud_cover"] = clouds,
                ["weather_code"] = codes
            };

            var hours = JsonSerializer.SerializeToElement(payload).Clone();
            return new CachedHours(
                hours,
                DateTimeOffset.UtcNow,
                "MET Norway (fallback)",
                6);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(
                ex,
                "MET Norway fallback forecast also failed for ({Latitude}, {Longitude}).",
                latitudeText,
                longitudeText);
            return null;
        }
    }

    private static int? MapMetNorwaySymbolToWmo(string? symbol)
    {
        if (string.IsNullOrWhiteSpace(symbol)) return null;
        var s = symbol.ToLowerInvariant();
        if (s.Contains("thunder")) return 95;
        if (s.Contains("heavyrain")) return 65;
        if (s.Contains("rainshowers")) return 80;
        if (s.Contains("lightrain")) return 61;
        if (s.Contains("rain")) return 63;
        if (s.Contains("fog")) return 45;
        if (s.Contains("snow")) return 73;
        if (s.Contains("cloudy") && !s.Contains("partly")) return 3;
        if (s.Contains("partlycloudy")) return 2;
        if (s.Contains("fair")) return 1;
        if (s.Contains("clearsky")) return 0;
        return null;
    }

    private async Task LogRateLimitAsync(HttpResponseMessage response, CancellationToken ct)
    {
        string? reason = null;
        try
        {
            const int maxBodyBytes = 4096;
            using var stream = await response.Content.ReadAsStreamAsync(ct);
            var buffer = new byte[maxBodyBytes + 1];
            var count = await stream.ReadAtLeastAsync(buffer, buffer.Length, false, ct);
            if (count == 0 || count > maxBodyBytes)
                return;

            using var body = JsonDocument.Parse(buffer.AsMemory(0, count));
            if (body.RootElement.ValueKind == JsonValueKind.Object &&
                body.RootElement.TryGetProperty("reason", out var field) &&
                field.ValueKind == JsonValueKind.String &&
                field.GetString() is { } message)
            {
                reason = string.Concat(message.Take(256).Where(c => !char.IsControl(c)));
            }
        }
        finally
        {
            logger.LogWarning(
                "Open-Meteo HTTP 429. Reason: {Reason}. Retry-After: {RetryAfter}",
                string.IsNullOrWhiteSpace(reason) ? "unavailable" : reason,
                response.Headers.RetryAfter?.ToString() ?? "not provided");
        }
    }

    // =========================================================
    // FORECAST MAPPING
    // =========================================================

    private static void MapForecastToPoint(
        WaypointWeather weatherPoint,
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

        var targetTime =
            weatherPoint.EstimatedArrivalTime
            ?? flightStart;

        var selectedIndex = -1;

        var slotSpanHours = Math.Max(1, cachedHours.MaxSlotHours);

        for (var index = 0;
             index < times.GetArrayLength();
             index++)
        {
            if (!DateTime.TryParse(
                    times[index].GetString(),
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal |
                    DateTimeStyles.AdjustToUniversal,
                    out var forecastTime))
            {
                continue;
            }

            var slotEnd = forecastTime.AddHours(slotSpanHours);
            if (index + 1 < times.GetArrayLength() &&
                DateTime.TryParse(
                    times[index + 1].GetString(),
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal |
                    DateTimeStyles.AdjustToUniversal,
                    out var nextForecastTime) &&
                nextForecastTime > forecastTime &&
                nextForecastTime <= slotEnd)
            {
                slotEnd = nextForecastTime;
            }

            if (forecastTime <= targetTime &&
                slotEnd > targetTime)
            {
                selectedIndex = index;
                break;
            }
        }

        if (selectedIndex < 0)
        {
            return;
        }

        decimal? Metric(string name)
        {
            if (!hours.TryGetProperty(
                    name,
                    out var values) ||
                values.ValueKind !=
                JsonValueKind.Array ||
                selectedIndex >=
                values.GetArrayLength())
            {
                return null;
            }

            var value =
                values[selectedIndex];

            return value.ValueKind ==
                   JsonValueKind.Number &&
                   value.TryGetDecimal(
                       out var number)
                ? number
                : null;
        }

        if (DateTime.TryParse(
                times[selectedIndex].GetString(),
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal |
                DateTimeStyles.AdjustToUniversal,
                out var forecastAt))
        {
            weatherPoint.ForecastTime =
                forecastAt;
        }

        weatherPoint.TemperatureCelsius =
            Metric("temperature_2m");

        weatherPoint.RelativeHumidityPercent =
            Metric("relative_humidity_2m");

        weatherPoint.WindSpeedMetersPerSecond =
            Metric("wind_speed_10m");

        weatherPoint.WindDirectionDegrees =
            Metric("wind_direction_10m");

        weatherPoint.WindGustMetersPerSecond =
            Metric("wind_gusts_10m");

        weatherPoint.PrecipitationProbabilityPercent =
            Metric("precipitation_probability");

        weatherPoint.PrecipitationMillimeters =
            Metric("rain");

        weatherPoint.VisibilityMeters =
            Metric("visibility");

        weatherPoint.CloudCoverPercent =
            Metric("cloud_cover");

        weatherPoint.WeatherCode =
            (int?)Metric("weather_code");

        var complete =
            Variables.All(variable =>
                Metric(variable).HasValue);

        if (stale)
        {
            weatherPoint.Status = "Stale";
        }
        else
        {
            weatherPoint.Status =
                complete
                    ? "Available"
                    : "Partial";
        }

        if (weatherPoint.Status != "Available")
        {
            weatherPoint.Warnings.Add(
                $"{PointLabel(weatherPoint)}: " +
                $"forecast is " +
                $"{weatherPoint.Status.ToLowerInvariant()}.");
        }
    }

    // =========================================================
    // ETA
    // =========================================================

    private static DateTime? EstimateArrivalTime(
        Mission mission,
        double distanceFromStart,
        double totalDistance)
    {
        if (!mission.StartTime.HasValue ||
            !mission.EndTime.HasValue)
        {
            return null;
        }

        if (totalDistance <= 0)
        {
            return mission.StartTime.Value;
        }

        var fraction =
            Math.Clamp(
                distanceFromStart /
                totalDistance,
                0,
                1);

        var duration =
            mission.EndTime.Value -
            mission.StartTime.Value;

        return mission.StartTime.Value +
               TimeSpan.FromTicks(
                   (long)(
                       duration.Ticks *
                       fraction));
    }

    // =========================================================
    // WIND COMPONENTS
    // =========================================================

    private static void CalculateWindComponents(
        WaypointWeather weatherPoint)
    {
        if (!weatherPoint
                .WindSpeedMetersPerSecond
                .HasValue ||
            !weatherPoint
                .WindDirectionDegrees
                .HasValue ||
            !weatherPoint
                .SegmentBearingDegrees
                .HasValue)
        {
            return;
        }

        var windSpeed =
            (double)weatherPoint
                .WindSpeedMetersPerSecond.Value;

        /*
         * Open-Meteo wind direction là hướng gió thổi đến từ đâu.
         * So trực tiếp với bearing để tính headwind/crosswind.
         */
        var difference =
            NormalizeSignedDegrees(
                (double)weatherPoint
                    .WindDirectionDegrees.Value -
                (double)weatherPoint
                    .SegmentBearingDegrees.Value);

        var radians =
            DegreesToRadians(
                difference);

        weatherPoint.CrosswindMetersPerSecond =
            (decimal)Math.Abs(
                windSpeed *
                Math.Sin(radians));

        weatherPoint.HeadwindMetersPerSecond =
            (decimal)(
                windSpeed *
                Math.Cos(radians));
    }

    // =========================================================
    // RISK ENGINE
    // =========================================================

    private static void CalculateRisk(
        WaypointWeather weather,
        MissionWeatherOptions policy)
    {
        if (weather.Status == "Unavailable")
        {
            weather.RiskLevel = "Unknown";
            weather.RiskScore = 0;
            return;
        }

        var score = 0;
        var autoHigh = false;

        void AddReason(string reason)
        {
            weather.RiskReasons.Add(reason);
            weather.Warnings.Add(reason);
        }

        void AddIncreasingMetric(
            string name,
            decimal? value,
            decimal reviewThreshold,
            decimal highThreshold,
            int weight,
            string unit,
            bool highAutomatically)
        {
            if (!value.HasValue)
            {
                return;
            }

            if (value >= highThreshold)
            {
                score += weight;

                AddReason(
                    $"{PointLabel(weather)}: {name} " +
                    $"{value:0.##} {unit} reaches the high-risk " +
                    $"threshold {highThreshold:0.##} {unit}.");

                autoHigh |= highAutomatically;
                return;
            }

            if (value < reviewThreshold)
            {
                return;
            }

            var range =
                highThreshold -
                reviewThreshold;

            var ratio =
                range <= 0
                    ? 1
                    : (value.Value -
                       reviewThreshold) /
                      range;

            var contribution =
                (int)Math.Ceiling(
                    weight *
                    (0.5m +
                     0.5m *
                     Math.Clamp(
                         ratio,
                         0,
                         1)));

            score += contribution;

            AddReason(
                $"{PointLabel(weather)}: {name} " +
                $"{value:0.##} {unit} exceeds the review " +
                $"threshold {reviewThreshold:0.##} {unit}.");
        }

        AddIncreasingMetric(
            "wind",
            weather.WindSpeedMetersPerSecond,
            policy.WindReviewMetersPerSecond,
            policy.WindHighMetersPerSecond,
            policy.WindWeight,
            "m/s",
            true);

        AddIncreasingMetric(
            "gust",
            weather.WindGustMetersPerSecond,
            policy.GustReviewMetersPerSecond,
            policy.GustHighMetersPerSecond,
            policy.GustWeight,
            "m/s",
            true);

        AddIncreasingMetric(
            "crosswind",
            weather.CrosswindMetersPerSecond,
            policy.CrosswindReviewMetersPerSecond,
            policy.CrosswindHighMetersPerSecond,
            policy.CrosswindWeight,
            "m/s",
            true);

        AddIncreasingMetric(
            "rain",
            weather.PrecipitationMillimeters,
            policy.RainReviewMillimetersPerHour,
            policy.RainHighMillimetersPerHour,
            policy.RainWeight,
            "mm/hour",
            true);

        AddIncreasingMetric(
            "rain probability",
            weather.PrecipitationProbabilityPercent,
            policy.RainProbabilityReviewPercent,
            policy.RainProbabilityHighPercent,
            policy.RainProbabilityWeight,
            "%",
            true);

        // Visibility: càng thấp càng nguy hiểm.
        if (weather.VisibilityMeters.HasValue)
        {
            if (weather.VisibilityMeters <=
                policy.VisibilityHighMeters)
            {
                score += policy.VisibilityWeight;
                autoHigh = true;

                AddReason(
                    $"{PointLabel(weather)}: visibility " +
                    $"{weather.VisibilityMeters:0} m is below " +
                    $"the high-risk limit " +
                    $"{policy.VisibilityHighMeters:0} m.");
            }
            else if (weather.VisibilityMeters <=
                     policy.VisibilityReviewMeters)
            {
                score +=
                    (int)Math.Ceiling(
                        policy.VisibilityWeight *
                        0.6m);

                AddReason(
                    $"{PointLabel(weather)}: visibility " +
                    $"{weather.VisibilityMeters:0} m requires review.");
            }
        }

        // Temperature.
        if (weather.TemperatureCelsius.HasValue)
        {
            var temperature =
                weather.TemperatureCelsius.Value;

            if (temperature <
                    policy.OperatingMinTemperatureCelsius ||
                temperature >
                    policy.OperatingMaxTemperatureCelsius)
            {
                score += policy.TemperatureWeight;
                autoHigh = true;

                AddReason(
                    $"{PointLabel(weather)}: temperature " +
                    $"{temperature:0.##}°C is outside the configured " +
                    "operating range.");
            }
            else if (
                temperature <
                    policy.RecommendedMinTemperatureCelsius ||
                temperature >
                    policy.RecommendedMaxTemperatureCelsius)
            {
                score += policy.TemperatureWeight;

                AddReason(
                    $"{PointLabel(weather)}: temperature " +
                    $"{temperature:0.##}°C is outside the recommended range.");
            }
        }

        AddIncreasingMetric(
            "humidity",
            weather.RelativeHumidityPercent,
            policy.HumidityReviewPercent,
            policy.HumidityHighPercent,
            policy.HumidityWeight,
            "%",
            false);

        AddIncreasingMetric(
            "cloud cover",
            weather.CloudCoverPercent,
            policy.CloudCoverReviewPercent,
            policy.CloudCoverHighPercent,
            policy.CloudCoverWeight,
            "%",
            false);

        ApplyWeatherCodeRisk(
            weather,
            policy,
            ref score,
            ref autoHigh);

        weather.RiskScore =
            Math.Clamp(
                score,
                0,
                100);

        weather.RiskLevel =
            autoHigh ||
            weather.RiskScore >=
            policy.HighRiskScore
                ? "High"
                : weather.RiskScore >=
                  policy.ReviewRiskScore
                    ? "Review"
                    : "Low";
    }

    private static void ApplyWeatherCodeRisk(
        WaypointWeather weather,
        MissionWeatherOptions policy,
        ref int score,
        ref bool autoHigh)
    {
        if (!weather.WeatherCode.HasValue)
        {
            return;
        }

        var code =
            weather.WeatherCode.Value;

        string? reason = null;
        var high = false;

        if (code is 95 or 96 or 99)
        {
            reason = "thunderstorm forecast";
            high = true;
        }
        else if (code is 66 or 67)
        {
            reason = "freezing rain forecast";
            high = true;
        }
        else if (code is 75 or 77 or 82 or 86)
        {
            reason = "severe precipitation forecast";
            high = true;
        }
        else if (code is 45 or 48)
        {
            reason = "fog forecast";
        }
        else if (
            code is >= 51 and <= 65 ||
            code is >= 71 and <= 73 ||
            code is 80 or 81 or 85)
        {
            reason = "adverse precipitation forecast";
        }

        if (reason is null)
        {
            return;
        }

        score +=
            high
                ? policy.WeatherCodeWeight
                : (int)Math.Ceiling(
                    policy.WeatherCodeWeight *
                    0.6m);

        autoHigh |= high;

        var message =
            $"{PointLabel(weather)}: {reason} " +
            $"(WMO code {code}).";

        weather.RiskReasons.Add(message);
        weather.Warnings.Add(message);
    }

    // =========================================================
    // OVERALL RESULT
    // =========================================================

    private static string CalculateOverallStatus(
        int routePointCount,
        List<WaypointWeather> weatherPoints)
    {
        if (routePointCount == 0 ||
            weatherPoints.All(x =>
                x.Status == "Unavailable"))
        {
            return "Unavailable";
        }

        if (weatherPoints.Any(x =>
                x.Status == "Stale"))
        {
            return "Stale";
        }

        if (weatherPoints.All(x =>
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

        if (result.Waypoints.Any(x =>
                x.RiskLevel == "Review"))
        {
            return "Review";
        }

        if (result.Waypoints.All(x =>
                x.RiskLevel == "Unknown"))
        {
            return "Unknown";
        }

        return "Low";
    }

    // =========================================================
    // GEOMETRY
    // =========================================================

    private static double HaversineDistanceMeters(
        decimal latitudeA,
        decimal longitudeA,
        decimal latitudeB,
        decimal longitudeB)
    {
        const double earthRadiusMeters =
            6_371_000;

        var lat1 =
            DegreesToRadians(
                (double)latitudeA);

        var lat2 =
            DegreesToRadians(
                (double)latitudeB);

        var deltaLatitude =
            DegreesToRadians(
                (double)(
                    latitudeB -
                    latitudeA));

        var deltaLongitude =
            DegreesToRadians(
                (double)(
                    longitudeB -
                    longitudeA));

        var value =
            Math.Sin(deltaLatitude / 2) *
            Math.Sin(deltaLatitude / 2) +
            Math.Cos(lat1) *
            Math.Cos(lat2) *
            Math.Sin(deltaLongitude / 2) *
            Math.Sin(deltaLongitude / 2);

        var centralAngle =
            2 *
            Math.Atan2(
                Math.Sqrt(value),
                Math.Sqrt(1 - value));

        return earthRadiusMeters *
               centralAngle;
    }

    private static decimal CalculateBearingDegrees(
        decimal latitudeA,
        decimal longitudeA,
        decimal latitudeB,
        decimal longitudeB)
    {
        var lat1 =
            DegreesToRadians(
                (double)latitudeA);

        var lat2 =
            DegreesToRadians(
                (double)latitudeB);

        var deltaLongitude =
            DegreesToRadians(
                (double)(
                    longitudeB -
                    longitudeA));

        var y =
            Math.Sin(deltaLongitude) *
            Math.Cos(lat2);

        var x =
            Math.Cos(lat1) *
            Math.Sin(lat2) -
            Math.Sin(lat1) *
            Math.Cos(lat2) *
            Math.Cos(deltaLongitude);

        var bearing =
            RadiansToDegrees(
                Math.Atan2(y, x));

        return (decimal)(
            (bearing + 360) % 360);
    }

    private static double DegreesToRadians(
        double value)
    {
        return value *
               Math.PI /
               180;
    }

    private static double RadiansToDegrees(
        double value)
    {
        return value *
               180 /
               Math.PI;
    }

    private static double NormalizeSignedDegrees(
        double value)
    {
        value %= 360;

        if (value > 180)
        {
            value -= 360;
        }
        else if (value < -180)
        {
            value += 360;
        }

        return value;
    }

    // =========================================================
    // HELPERS
    // =========================================================

    private static decimal? Maximum(
        IEnumerable<decimal?> values)
    {
        var available = values
            .Where(x => x.HasValue)
            .Select(x => x!.Value)
            .ToList();

        return available.Count == 0
            ? null
            : available.Max();
    }

    private static string PointLabel(
        WaypointWeather point)
    {
        if (point.IsRouteSample)
        {
            return
                $"Route sample {point.SampleOrder} " +
                $"on segment {point.SegmentFrom}-{point.SegmentTo}";
        }

        return $"WP{point.SequenceOrder}";
    }
}