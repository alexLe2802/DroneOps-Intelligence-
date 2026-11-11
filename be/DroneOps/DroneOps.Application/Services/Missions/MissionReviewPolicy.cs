using DroneOps.Application.DTOs.Response.Missions;
using DroneOps.Domain.Entities;

namespace DroneOps.Application.Services.Missions;

public static class MissionReviewPolicy
{
    public static bool CanEdit(string status) => status is
        "Draft" or "Rejected" or "Pending" or "Pending Review" or "Submitted" or "Approved" or "Pending Confirm";

    public static bool IsHighRisk(MissionValidationResult? result) =>
        string.Equals(result?.Weather?.OverallRisk, "High", StringComparison.OrdinalIgnoreCase) ||
        result?.Battery?.RequiresConfirmation == true;

    public static bool NeedsConfirmation(MissionUpdateRequest request, MissionValidationResult? result) =>
        IsHighRisk(result) && (request.PilotConfirmedValidationRunId != request.ValidationRunId ||
                              request.PilotConfirmedBy is null || request.PilotConfirmedAt is null);

    public static string SafetyFingerprint(MissionValidationResult result) => System.Text.Json.JsonSerializer.Serialize(new
    {
        result.Weather?.OverallRisk, result.Weather?.MaxWindMetersPerSecond,
        result.Weather?.MaxGustMetersPerSecond, result.Weather?.MaxRainMillimetersPerHour,
        result.Weather?.ThunderstormForecast, result.Weather?.MaxCrosswindMetersPerSecond,
        WeatherWarnings = result.Weather?.Warnings,
        result.Battery?.RequiresConfirmation, result.Battery?.RequiredBatteryPercent,
        result.Battery?.SafetyReservePercent, result.Battery?.RiskLevel
    });

    public static void ClearConfirmation(MissionUpdateRequest request)
    {
        request.PilotConfirmedBy = null;
        request.PilotConfirmedAt = null;
        request.PilotConfirmedValidationRunId = null;
    }
}
