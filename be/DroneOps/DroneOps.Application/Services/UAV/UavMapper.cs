using System.Text.Json;
using DroneOps.Application.DTOs.Response.Common;
using DroneOps.Application.DTOs.Response.UAVs;
using DroneOps.Domain.Entities;

namespace DroneOps.Application.Services.UAVs;

/// <summary>
/// Centralized mapping and paging helpers for UAV records so schema/DTO updates happen in one place.
/// </summary>
internal static class UavMapper
{
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    public static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public static (int PageNumber, int PageSize) NormalizePaging(int pageNumber, int pageSize)
    {
        var safePage = pageNumber < 1 ? 1 : pageNumber;
        var safeSize = pageSize switch
        {
            < 1 => 10,
            > 100 => 100,
            _ => pageSize
        };
        return (safePage, safeSize);
    }

    public static PagedResponse<UavResponse> CreatePagedResponse(
        IReadOnlyList<UavResponse> items,
        int totalCount,
        int pageNumber,
        int pageSize)
    {
        var totalPages = totalCount == 0
            ? 0
            : (int)Math.Ceiling(totalCount / (double)pageSize);

        return new PagedResponse<UavResponse>
        {
            PageNumber = pageNumber,
            PageSize = pageSize,
            TotalItems = totalCount,
            TotalPages = totalPages,
            Items = items
        };
    }

    public static UavResponse ToResponse(UAV uav, string pilotName)
    {
        return new UavResponse
        {
            HasPendingProposal = uav.HasPendingProposal,
            RegistryVersion = uav.RegistryVersion,
            HasApprovedInformation = UavReviewWorkflow.IsApproved(uav),
            Id = uav.Id,
            PilotId = uav.PilotId,
            PilotName = pilotName,

            // Basic information
            Code = uav.Code,
            Name = uav.Name,
            Manufacturer = uav.Manufacturer,
            Model = uav.Model,
            SerialNumber = uav.SerialNumber,
            Status = uav.Status,

            // Physical specifications
            EmptyWeightKg = uav.EmptyWeightKg,
            MaxTakeoffWeightKg = uav.MaxTakeoffWeightKg,
            MaxPayloadKg = uav.MaxPayloadKg,

            // Battery specifications
            BatteryType = uav.BatteryType,
            BatteryCapacityMah = uav.BatteryCapacityMah,
            BatteryVoltage = uav.BatteryVoltage,
            BatteryMaxVoltage = uav.BatteryMaxVoltage,
            FirmwareNavigationDefaults = uav.FirmwareNavigationDefaultsJson is null
                ? null
                : JsonSerializer.Deserialize<FirmwareParameterSnapshot>(uav.FirmwareNavigationDefaultsJson, WebJson),
            BatteryCellCount = uav.BatteryCellCount,

            // Flight capabilities
            MaxFlightTimeMinutes = uav.MaxFlightTimeMinutes,
            MaxRangeKm = uav.MaxRangeKm,
            MaxSpeedKmh = uav.MaxSpeedKmh,
            MaxWindSpeedMps = uav.MaxWindSpeedMps,
            MaxOperatingAltitudeMeters = uav.MaxOperatingAltitudeMeters,

            // Operational information
            PurchaseDate = uav.PurchaseDate,
            LastMaintenanceAt = uav.LastMaintenanceAt,
            NextMaintenanceAt = uav.NextMaintenanceAt,
            TotalFlightHours = uav.TotalFlightHours,
            TotalFlights = uav.TotalFlights,

            // Media
            ImageUrl = string.IsNullOrWhiteSpace(uav.ImageStoragePath)
                ? null
                : $"/api/uavs/{uav.Id}/image?v={Uri.EscapeDataString(uav.ImageStoragePath)}",
            ImageModerationStatus = uav.ImageModerationStatus,
            ImageModeratedAt = uav.ImageModeratedAt,
            ImageModerationReason = uav.ImageModerationReason,
            CreatedAt = uav.CreatedAt
        };
    }
}
