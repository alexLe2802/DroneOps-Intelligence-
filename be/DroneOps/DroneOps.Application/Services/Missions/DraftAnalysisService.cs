using System.Text.Json;
using DroneOps.Application.DTOs.Request.Missions;
using DroneOps.Application.Interfaces.AI;
using DroneOps.Application.Interfaces.Missions;
using DroneOps.Application.Validators.Missions;
using DroneOps.Domain.Entities;
using DroneOps.Persistence.Data;
using Microsoft.EntityFrameworkCore;

namespace DroneOps.Application.Services.Missions;

public sealed class DraftAnalysisService(AppDbContext db, IMissionWeatherQueryService weather, IAIService ai)
{
    public async Task<object> AnalyzeAsync(DraftAnalysisRequest r, Guid actor, CancellationToken ct)
    {
        if (r.UavId == Guid.Empty || r.Name?.Trim().Length is null or < 2 or > 200 ||
            r.StartTime <= DateTimeOffset.UtcNow || r.EndTime <= r.StartTime)
            throw new ArgumentException("Select an active aircraft, name the mission and enter a valid future schedule.");
        if (r.Waypoints is not { Count: >= 2 and <= 500 } || r.Waypoints.Where((w, i) =>
            w == null || w.SequenceOrder != i + 1 || w.Latitude is null or < -90 or > 90 || w.Longitude is null or < -180 or > 180 ||
            w.Altitude is null or <= 10 or > 100000 || (i == 0 ? w.ActionType != "TakeOff" :
            i == r.Waypoints.Count - 1 ? w.ActionType is not ("Land" or "Return") : w.ActionType is not ("FlyThrough" or "Hover"))).Any())
            throw new ArgumentException("Enter a valid ordered route with takeoff and a final Land or Return waypoint.");
        if (r.PayloadKg < 0 || r.Cruise is < 0.1m or > 1000 || r.Ceiling is < 0.1m or > 1000)
            throw new ArgumentException("Check payload and speed inputs.");

        var uav = await db.UAVs.AsNoTracking().SingleOrDefaultAsync(u => u.Id == r.UavId &&
            u.Pilot.UserId == actor && u.Status == "Active", ct)
            ?? throw new KeyNotFoundException("Active aircraft unavailable for this pilot.");
        var automatic = r.Automatic == true;
        var limits = automatic ? r.Limits ?? AutomaticFlightPolicy.DefaultLimits() : null;
        var findings = new List<string>();
        var hardwareCap = uav.MaxSpeedKmh > 0 ? decimal.Floor(uav.MaxSpeedKmh.Value / 3.6m * 100) / 100 : (decimal?)null;
        decimal? ceiling = hardwareCap;
        if (ceiling.HasValue && automatic)
            ceiling = Math.Min(ceiling.Value, limits!.PolicyVersion == "multicopter-envelope-v2" ? AutomaticFlightPolicy.MaximumHorizontal : 5m);
        if (ceiling.HasValue && r.Ceiling.HasValue) ceiling = Math.Min(ceiling.Value, r.Ceiling.Value);
        decimal? cruise = r.Cruise ?? (automatic && limits!.PolicyVersion == "multicopter-envelope-v2" ? AutomaticFlightPolicy.DefaultCruise : null);
        if (ceiling.HasValue && cruise.HasValue) cruise = Math.Min(cruise.Value, ceiling.Value);
        if (r.Cruise > ceiling || r.Ceiling > hardwareCap)
            findings.Add("Tốc độ đã nhập vượt giới hạn. Hãy kiểm tra giá trị đề xuất trước khi áp dụng.");
        if (cruise.HasValue && ceiling.HasValue && automatic && cruise < 0.5m)
        {
            cruise = null;
            findings.Add("Giới hạn tốc độ thấp hơn mức tối thiểu của chế độ tự động. Kiểm tra lại UAV và chế độ bay.");
        }
        if (!ceiling.HasValue) findings.Add("Thiếu tốc độ tối đa trong hồ sơ UAV để đối chiếu giới hạn.");
        if (!cruise.HasValue) findings.Add("Nhập tốc độ hành trình để ước tính thời gian bay.");
        if (r.Automatic == null) findings.Add("Chọn chế độ bay để phân tích theo cấu hình phù hợp.");
        if (r.PayloadKg == null) findings.Add("Chưa khai báo tải thực mang. Khả năng mang tải chưa được đánh giá.");
        else if (uav.MaxPayloadKg == null || uav.EmptyWeightKg == null || uav.MaxTakeoffWeightKg == null)
            findings.Add("Hồ sơ UAV thiếu thông số để kiểm tra đầy đủ tải và khối lượng cất cánh.");
        if (r.PayloadKg > uav.MaxPayloadKg || uav.EmptyWeightKg + r.PayloadKg > uav.MaxTakeoffWeightKg)
            findings.Add("Khuyến cáo hoãn chuyến: tải hoặc khối lượng tối thiểu khi cất cánh vượt giới hạn UAV.");

        var plan = MissionPlans.Propose(new Mission { Id = Guid.Empty, CreatedBy = actor }, new CreateMissionRequest {
            Name = r.Name!, UavId = uav.Id, StartTime = r.StartTime.UtcDateTime, EndTime = (r.EndTime ?? r.StartTime).UtcDateTime,
            Waypoints = r.Waypoints.Select(w => new CreateWaypointRequest {
                SequenceOrder = w.SequenceOrder, Latitude = w.Latitude!.Value, Longitude = w.Longitude!.Value,
                Altitude = w.Altitude, ActionType = w.ActionType, Navigation = w.Navigation }).ToList(),
            AutomaticFlightRequested = automatic, AutomaticFlightLimits = limits,
            AltitudeReference = r.AltitudeReference, AverageSpeedMetersPerSecond = cruise ?? 0, MaxSpeedMetersPerSecond = ceiling ?? 0 });
        var policyErrors = automatic ? AutomaticFlightPolicy.Errors(true, r.AltitudeReference, cruise ?? 0, ceiling ?? 0, limits,
            plan.Waypoints.Select(w => (w.Altitude, w.ActionType))).Concat(AutomaticFlightPolicy.NavigationErrors(
            plan.Waypoints.Select(w => (w.Navigation, w.ActionType)), ceiling ?? 0)).ToList() : [];
        findings.AddRange(policyErrors);
        DateTimeOffset? estimatedEnd = null;
        if (cruise >= 0.1m && policyErrors.Count == 0)
        {
            var minutes = MissionBatteryRiskCalculator.EstimateMinutes(plan, cruise.Value);
            if (minutes is > 0 and <= 1440 && r.StartTime.Year < 9999)
            {
                estimatedEnd = r.StartTime.AddMinutes((double)decimal.Ceiling(minutes));
                if (r.EndTime < estimatedEnd) findings.Add("Giờ kết thúc đã nhập sớm hơn thời điểm hoàn thành ước tính.");
                if (minutes > uav.MaxFlightTimeMinutes)
                    findings.Add("Khuyến cáo hoãn chuyến: thời lượng ước tính vượt thời gian bay tối đa của UAV.");
            }
        }
        if (uav.MaxFlightTimeMinutes is null or <= 0)
            findings.Add("Thiếu thời gian bay tối đa trong hồ sơ UAV để đối chiếu thời lượng.");
        plan.Name = ""; // Renaming a draft does not change the route forecast.
        plan.EndTime = (estimatedEnd ?? r.EndTime ?? r.StartTime).UtcDateTime;
        var forecast = await weather.ReadDraftAsync(plan, ct);
        if (estimatedEnd == null && r.EndTime == null)
            findings.Add("Dự báo dùng giờ bắt đầu cho cả tuyến vì chưa có thời lượng hoặc giờ kết thúc.");
        if (forecast.Status != "Available") findings.Add($"Dự báo {forecast.Status}: chưa đủ dữ liệu thời tiết hiện hành cho toàn tuyến.");
        const decimal defaultWindLimitMps = 8.5m;
        decimal? windLimit = uav.MaxWindSpeedMps ?? defaultWindLimitMps;
        if (uav.MaxWindSpeedMps == null)
            findings.Add($"UAV wind limit is not recorded; using default {defaultWindLimitMps} m/s for wind checks.");
        if (automatic) windLimit = Math.Min(windLimit.Value, limits!.MaxWindMetersPerSecond);
        var gustLimit = automatic ? limits!.MaxGustMetersPerSecond : (decimal?)null;
        if (automatic && forecast.Waypoints.Any(w => w.WindGustMetersPerSecond == null))
            findings.Add("Thiếu dự báo gió giật tại một số điểm để đối chiếu giới hạn bay tự động.");
        var windAlerts = forecast.Waypoints.Where(w => forecast.Status != "Stale" && (w.Status is "Available" or "Partial") &&
            (w.WindSpeedMetersPerSecond > windLimit || w.WindGustMetersPerSecond > gustLimit || w.RiskLevel == "High"))
            .Select(w => new { w.SequenceOrder, w.IsReturnLeg, w.SegmentFrom, w.SegmentTo, w.EstimatedArrivalTime,
                w.ForecastTime, w.WindSpeedMetersPerSecond, w.WindGustMetersPerSecond, w.RiskLevel }).ToList();
        if (windAlerts.Count > 0) findings.Add("Khuyến cáo hoãn chuyến và kiểm tra lại các điểm có cảnh báo thời tiết.");
        var recommendedLimits = automatic ? RecommendLimits(limits!, uav, r, forecast.MaxWindMetersPerSecond, forecast.MaxGustMetersPerSecond) : null;
        var cruiseBasis = r.Cruise.HasValue ? "Pilot input, limited by the known ceiling" :
            cruise.HasValue ? "Automatic flight policy default, limited by the known ceiling" : null;
        var evidence = new { r.Name, r.StartTime, r.EndTime, waypointCount = r.Waypoints.Count, cruiseBasis, uav.Model, uav.EmptyWeightKg, uav.MaxTakeoffWeightKg, uav.MaxPayloadKg, r.PayloadKg,
            r.Automatic, cruise, ceiling, estimatedEnd, windLimit, gustLimit, recommendedLimits, forecast.Status, forecast.Provider,
            forecast.FetchedAt, forecast.MaxWindMetersPerSecond, forecast.MaxGustMetersPerSecond, windAlerts, findings };
        string? explanation;
        using var explanationBudget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        explanationBudget.CancelAfter(TimeSpan.FromSeconds(25));
        try { explanation = await ai.ExplainDraftAsync(JsonSerializer.Serialize(evidence), explanationBudget.Token); }
        catch (Exception e) when (!ct.IsCancellationRequested && e is InvalidOperationException or HttpRequestException or JsonException or OperationCanceledException)
        { explanation = null; }
        return new { cruise, cruiseBasis, ceiling, estimatedEnd, findings, windAlerts, windLimit, gustLimit, recommendedLimits,
            weatherStatus = forecast.Status, forecast.Provider, forecast.FetchedAt, explanation };
    }

    // Deterministic advisory values; the pilot chooses what to apply and mission validation still runs separately.
    private static AutomaticFlightLimits RecommendLimits(AutomaticFlightLimits limits, UAV uav, DraftAnalysisRequest r,
        decimal? forecastWind, decimal? forecastGust)
    {
        var v2 = limits.PolicyVersion == "multicopter-envelope-v2";
        var climbCap = v2 ? 8m : 1.5m;
        var descentCap = v2 ? 6m : 1m;
        var landingCap = v2 ? 1.5m : 0.7m;
        var baseTakeoff = v2 ? 6.5m : 1m;
        var baseClimb = v2 ? 6.5m : 1m;
        var baseDescent = v2 ? 5.8m : 0.7m;
        var baseLanding = v2 ? 1m : 0.7m;
        var scale = 1m;
        if (r.PayloadKg is > 0 && uav.MaxPayloadKg is > 0)
        {
            var ratio = Math.Min(1m, r.PayloadKg.Value / uav.MaxPayloadKg.Value);
            if (ratio >= 0.75m) scale = Math.Min(scale, 0.75m);
            else if (ratio >= 0.5m) scale = Math.Min(scale, 0.85m);
        }
        if (forecastWind >= 2.5m || forecastGust >= 4m) scale = Math.Min(scale, 0.85m);
        static decimal Round(decimal value) => Math.Round(value, 1, MidpointRounding.AwayFromZero);
        var climb = Math.Clamp(Round(baseClimb * scale), 1m, climbCap);
        var takeoff = Math.Clamp(Round(baseTakeoff * scale), 1m, climb);
        var descent = Math.Clamp(Round(baseDescent * scale), 0.6m, descentCap);
        var landing = Math.Clamp(Round(baseLanding * (scale < 1m ? 0.9m : 1m)), 0.6m, Math.Min(landingCap, descent));
        var altitudeCap = uav.MaxOperatingAltitudeMeters is > 10.1m
            ? Math.Min(AutomaticFlightPolicy.MaximumAltitude, uav.MaxOperatingAltitudeMeters.Value)
            : AutomaticFlightPolicy.MaximumAltitude;
        var highest = r.Waypoints.Max(w => w.Altitude ?? 0m);
        var altitude = Math.Clamp(Round(Math.Clamp(Math.Ceiling(highest + 5m), 10.1m, altitudeCap)), 10.1m, altitudeCap);
        var wind = Math.Clamp(Round(Math.Min(3m, uav.MaxWindSpeedMps is > 0 ? uav.MaxWindSpeedMps.Value : 3m)), 0.1m, 3m);
        var gust = uav.MaxWindSpeedMps is > 0 and < 3m ? Math.Clamp(Round(wind * 1.5m), wind, 5m) : 5m;
        return new AutomaticFlightLimits(takeoff, climb, descent, landing, altitude, wind, gust, limits.PolicyVersion);
    }
}
