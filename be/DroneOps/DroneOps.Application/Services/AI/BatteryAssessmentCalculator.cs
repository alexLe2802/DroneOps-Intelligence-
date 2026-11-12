using DroneOps.Application.DTOs.Response.AI;
using DroneOps.Application.DTOs.Response.Missions;
using DroneOps.Application.Validators.Missions;
using DroneOps.Domain.Entities;
using System.Text.Json;

namespace DroneOps.Application.Services.AI;

public static class BatteryAssessmentCalculator
{
    public static BatteryAssessmentResponse Calculate(Mission mission, UAV? uav, TelemetryRecord? telemetry,
        WeatherAdvisory? weather, DateTimeOffset now)
    {
        var result = new BatteryAssessmentResponse { ConfiguredCapacityMah = uav?.BatteryCapacityMah is > 0 ? uav.BatteryCapacityMah : null };
        if (uav == null || uav.Id != mission.UAVId)
        {
            result.MissingData.Add("The mission has no matching UAV specification.");
            return result;
        }
        // Reuse the source-bound FC cache populated by the existing gateway poll.
        // Capacity is configuration, not a measured full-charge capacity.
        try
        {
            var snapshot = uav.FirmwareNavigationDefaultsJson == null ? null :
                JsonSerializer.Deserialize<FirmwareParameterSnapshot>(uav.FirmwareNavigationDefaultsJson, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            if (snapshot?.Values != null && !string.IsNullOrWhiteSpace(snapshot.HardwareIdentity)
                && snapshot.ObservedAtUtc >= now.AddMinutes(-1) && snapshot.ObservedAtUtc <= now.AddSeconds(2))
            {
                var capacity = snapshot.Values.GetValueOrDefault("BATT_CAPACITY", snapshot.Values.GetValueOrDefault("BAT1_CAPACITY"));
                if (capacity > 0 && capacity <= int.MaxValue && decimal.Truncate(capacity) == capacity)
                {
                    result.ConfiguredCapacityMah = (int)capacity;
                    result.CapacitySource = "FC MAVLink capacity parameter; configured, not measured";
                }
            }
        }
        catch (JsonException) { /* Missing/invalid optional FC cache uses the declared fallback. */ }
        var fresh = telemetry is { IsValid: true } && telemetry.UAVId == uav.Id && telemetry.MissionId == mission.Id
            && telemetry.SourceTimestamp >= now.AddSeconds(-15) && telemetry.SourceTimestamp <= now.AddSeconds(5)
            && telemetry.ReceivedAt >= now.AddSeconds(-15) && telemetry.ReceivedAt <= now.AddSeconds(5);
        if (!fresh)
            result.MissingData.Add("Fresh battery telemetry from this mission and UAV is required (maximum age 15 seconds).");
        else
        {
            result.TelemetrySourceTimestamp = telemetry!.SourceTimestamp;
            result.CurrentBatteryPercent = telemetry.BatteryLevel is >= 0 and <= 100 ? telemetry.BatteryLevel : null;
            result.ConsumedMah = telemetry.BatteryConsumedMah is >= 0 ? telemetry.BatteryConsumedMah : null;
            if (!result.CurrentBatteryPercent.HasValue) result.MissingData.Add("The flight controller is not reporting a valid remaining battery percentage.");
            if (telemetry.BatteryVoltageVolts is < 1m)
                result.MissingData.Add("Reported pack voltage is below 1 V. Verify battery power and voltage/current monitor calibration; reported percentage alone cannot establish available flight energy.");
            if (!result.ConsumedMah.HasValue) result.Warnings.Add("Consumed charge (mAh) is not reported by the flight controller; it is not inferred from voltage.");
        }
        if (!result.ConfiguredCapacityMah.HasValue)
            result.Warnings.Add("FC capacity is unavailable and no optional UAV capacity is supplied; mAh estimates are unavailable. FC percentage can still be assessed.");
        if (result.MissingData.Count != 0) return result;

        var requirement = MissionBatteryRiskCalculator.Calculate(mission, uav, weather);
        if (requirement.Status != "Available")
        {
            result.MissingData.AddRange(requirement.Warnings);
            return result;
        }
        result.Status = "EstimateOnly";
        result.RequiredBatteryPercent = requirement.RequiredBatteryPercent;
        if (result.ConfiguredCapacityMah is { } capacityMah)
        {
            result.EstimatedRequiredChargeMah = decimal.Round(capacityMah * requirement.RequiredBatteryPercent / 100m, 1);
            result.EstimatedRemainingChargeMah = decimal.Round(capacityMah * result.CurrentBatteryPercent!.Value / 100m, 1);
        }
        result.MeetsEstimatedRequirement = result.CurrentBatteryPercent!.Value >= requirement.RequiredBatteryPercent;
        result.Warnings.AddRange(requirement.Warnings);
        result.Warnings.Add("Charge estimates use the configured pack capacity, not a measured full-charge capacity. Confirm it matches the installed battery.");
        result.Warnings.Add("This reuses the route/endurance/weather heuristic with its 20% reserve. Hover, climb, payload and battery aging are not calibrated; this is not a flight authorization.");
        if (weather is null || weather.Status == "Unavailable")
            result.Warnings.Add("A current forecast is unavailable; the estimate has no forecast-based adjustment.");
        return result;
    }
}
