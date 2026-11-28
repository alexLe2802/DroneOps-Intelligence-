using DroneOps.Application.DTOs.Request.UAV;
using DroneOps.Application.DTOs.Request.UAVs;
using DroneOps.Application.DTOs.Response.Common;
using DroneOps.Application.DTOs.Response.UAVs;
using DroneOps.Application.Interfaces.Notifications;
using DroneOps.Application.Interfaces.Storage;
using DroneOps.Application.Interfaces.UAVs;
using DroneOps.Application.Validators;
using DroneOps.Domain.Entities;
using DroneOps.Persistence.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DroneOps.Application.Services.UAVs;

/// <summary>
/// Application facade for UAV registry CRUD, operational status transitions, and delegated image workflows.
/// Mapping, notifications, and image storage live in dedicated collaborators to keep future changes isolated.
/// </summary>
public sealed class UavService : IUavService
{
    private readonly IUavRepository _uavRepository;
    private readonly IPilotRepository _pilotRepository;
    private readonly UavReviewWorkflow? _reviews;
    private readonly UavNotificationHelper _notifications;
    private readonly UavImageWorkflow _images;

    public UavService(
        IUavRepository uavRepository,
        IPilotRepository pilotRepository,
        INotificationService notificationService,
        ILogger<UavService> logger,
        IUserMediaStorage storage,
        UavReviewWorkflow? reviews = null)
    {
        _uavRepository = uavRepository;
        _pilotRepository = pilotRepository;
        _reviews = reviews;
        _notifications = new UavNotificationHelper(notificationService, logger);
        _images = new UavImageWorkflow(uavRepository, storage, _notifications, logger);
    }

    // 1. Tạo mới UAV: Trạng thái mặc định là "Pending" (chờ duyệt)
    public async Task<UavResponse> CreateAsync(
        Guid userId,
        CreateUavRequest request,
        CancellationToken cancellationToken = default)
    {
        var pilot = await _pilotRepository.GetByUserIdAsync(userId, cancellationToken)
            ?? throw new InvalidOperationException("Pilot profile not found.");

        var code = request.Code.Trim();
        if (await _uavRepository.CodeExistsAsync(code, cancellationToken))
        {
            throw new InvalidOperationException("UAV code already exists.");
        }

        ValidateSpecifications(
            request.EmptyWeightKg,
            request.MaxTakeoffWeightKg,
            request.MaxPayloadKg,
            request.BatteryCapacityMah,
            request.BatteryVoltage,
            request.BatteryMaxVoltage,
            request.BatteryCellCount,
            request.MaxFlightTimeMinutes,
            request.MaxRangeKm,
            request.MaxSpeedKmh,
            request.MaxWindSpeedMps,
            request.MaxOperatingAltitudeMeters);

        var uav = new UAV
        {
            Id = Guid.NewGuid(),
            PilotId = pilot.Id,

            Code = code,
            Name = request.Name.Trim(),
            Manufacturer = UavMapper.Normalize(request.Manufacturer),
            Model = UavMapper.Normalize(request.Model),
            SerialNumber = UavMapper.Normalize(request.SerialNumber),

            EmptyWeightKg = request.EmptyWeightKg,
            MaxTakeoffWeightKg = request.MaxTakeoffWeightKg,
            MaxPayloadKg = request.MaxPayloadKg,

            BatteryType = UavMapper.Normalize(request.BatteryType),
            BatteryCapacityMah = request.BatteryCapacityMah,
            BatteryVoltage = request.BatteryVoltage,
            BatteryMaxVoltage = request.BatteryMaxVoltage,
            BatteryCellCount = request.BatteryCellCount,

            MaxFlightTimeMinutes = request.MaxFlightTimeMinutes,
            MaxRangeKm = request.MaxRangeKm,
            MaxSpeedKmh = request.MaxSpeedKmh,
            MaxWindSpeedMps = request.MaxWindSpeedMps ?? 8.5m,
            MaxOperatingAltitudeMeters = request.MaxOperatingAltitudeMeters,

            PurchaseDate = request.PurchaseDate,

            TotalFlightHours = 0,
            TotalFlights = 0,
            Status = "Pending",
            CreatedAt = DateTimeOffset.UtcNow
        };

        var document = request.ManufacturerDocument ?? throw new ArgumentException("Manufacturer documentation is required for new aircraft.");
        var attachment = document.ToDocument(userId);
        uav.Documents.Add(attachment);
        uav.ManufacturerDocumentId = attachment.Id;

        await _uavRepository.AddAsync(uav, cancellationToken);

        if (_reviews is not null)
        {
            uav.HasPendingProposal = true;
            _reviews.RecordRegistration(uav, userId);
        }

        try
        {
            await _uavRepository.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            throw new InvalidOperationException("UAV registration conflicts with an existing code. Reload before retrying.");
        }

        await _notifications.TryPublishToManagersAsync(
            "NewPendingUav",
            "New UAV Pending Approval",
            $"UAV '{uav.Code} - {uav.Name}' ({uav.Model ?? "unspecified model"}) has been submitted for approval.",
            userId,
            uav.Id,
            cancellationToken);

        return UavMapper.ToResponse(uav, pilot.User?.FullName ?? string.Empty);
    }

    // 2. Lấy danh sách UAV của Pilot đang đăng nhập
    public async Task<PagedResponse<UavResponse>> GetMyUavsAsync(
        Guid userId,
        GetAllUavsRequest request,
        CancellationToken cancellationToken = default)
    {
        var pilot = await _pilotRepository.GetByUserIdAsync(userId, cancellationToken);
        var (pageNumber, pageSize) = UavMapper.NormalizePaging(request.PageNumber, request.PageSize);

        if (pilot is null)
        {
            return UavMapper.CreatePagedResponse([], 0, pageNumber, pageSize);
        }

        var result = await _uavRepository.GetByPilotIdAsync(
            pilot.Id,
            UavMapper.Normalize(request.Keyword),
            UavMapper.Normalize(request.Status),
            pageNumber,
            pageSize,
            cancellationToken);

        var items = result.Items
            .Select(x => UavMapper.ToResponse(
                x,
                x.Pilot?.User?.FullName ?? pilot.User?.FullName ?? string.Empty))
            .ToList();

        return UavMapper.CreatePagedResponse(items, result.TotalCount, pageNumber, pageSize);
    }

    // 3. Cập nhật thông tin UAV (đề xuất sửa đổi qua UavReviewWorkflow)
    public async Task<bool> UpdateAsync(
        Guid userId,
        Guid uavId,
        UpdateUavRequest request,
        CancellationToken cancellationToken = default)
    {
        var uav = await _uavRepository.GetDetailAsync(uavId, cancellationToken);
        if (uav is null || uav.Pilot.UserId != userId)
        {
            return false;
        }

        var newCode = request.Code.Trim();
        if (!string.Equals(uav.Code, newCode, StringComparison.OrdinalIgnoreCase) &&
            await _uavRepository.CodeExistsAsync(newCode, cancellationToken))
        {
            throw new InvalidOperationException("UAV code already exists.");
        }

        ValidateSpecifications(
            request.EmptyWeightKg,
            request.MaxTakeoffWeightKg,
            request.MaxPayloadKg,
            request.BatteryCapacityMah,
            request.BatteryVoltage,
            request.BatteryMaxVoltage,
            request.BatteryCellCount,
            request.MaxFlightTimeMinutes,
            request.MaxRangeKm,
            request.MaxSpeedKmh,
            request.MaxWindSpeedMps,
            request.MaxOperatingAltitudeMeters);

        var normalizedName = request.Name.Trim();
        var normalizedManufacturer = UavMapper.Normalize(request.Manufacturer);
        var normalizedModel = UavMapper.Normalize(request.Model);
        var normalizedSerialNumber = UavMapper.Normalize(request.SerialNumber);
        var normalizedBatteryType = UavMapper.Normalize(request.BatteryType);

        var requiresReapproval = request.ManufacturerDocument != null ||
            !string.Equals(uav.Code, newCode, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(uav.Name, normalizedName, StringComparison.Ordinal) ||
            !string.Equals(uav.Manufacturer, normalizedManufacturer, StringComparison.Ordinal) ||
            !string.Equals(uav.Model, normalizedModel, StringComparison.Ordinal) ||
            !string.Equals(uav.SerialNumber, normalizedSerialNumber, StringComparison.Ordinal) ||
            uav.EmptyWeightKg != request.EmptyWeightKg ||
            uav.MaxTakeoffWeightKg != request.MaxTakeoffWeightKg ||
            uav.MaxPayloadKg != request.MaxPayloadKg ||
            !string.Equals(uav.BatteryType, normalizedBatteryType, StringComparison.Ordinal) ||
            uav.BatteryCapacityMah != request.BatteryCapacityMah ||
            uav.BatteryVoltage != request.BatteryVoltage ||
            uav.BatteryMaxVoltage != request.BatteryMaxVoltage ||
            uav.BatteryCellCount != request.BatteryCellCount ||
            uav.MaxFlightTimeMinutes != request.MaxFlightTimeMinutes ||
            uav.MaxRangeKm != request.MaxRangeKm ||
            uav.MaxSpeedKmh != request.MaxSpeedKmh ||
            uav.MaxWindSpeedMps != request.MaxWindSpeedMps ||
            uav.MaxOperatingAltitudeMeters != request.MaxOperatingAltitudeMeters ||
            uav.PurchaseDate != request.PurchaseDate;

        request.Code = newCode;
        request.Name = normalizedName;
        request.Manufacturer = normalizedManufacturer;
        request.Model = normalizedModel;
        request.SerialNumber = normalizedSerialNumber;
        request.BatteryType = normalizedBatteryType;

        if (_reviews is null)
        {
            throw new InvalidOperationException("Versioned UAV review is unavailable.");
        }

        await _reviews.StageAsync(uavId, userId, request, cancellationToken);

        if (requiresReapproval)
        {
            await _notifications.TryPublishToManagersAsync(
                "UavUpdatePending",
                "UAV Update Pending Approval",
                $"UAV '{uav.Code}' has a separate proposal awaiting review. Approved information is unchanged.",
                userId,
                uav.Id,
                cancellationToken);
        }

        return true;
    }

    // 4. Xóa UAV chưa gắn flight controller (UAV đã bind chỉ có thể deactivate)
    public async Task<bool> DeleteAsync(
        Guid userId,
        Guid uavId,
        bool canManageAll = false,
        CancellationToken cancellationToken = default)
    {
        var uav = await _uavRepository.GetDetailAsync(uavId, cancellationToken);
        var deleted = await _uavRepository.DeleteOwnedAsync(uavId, userId, canManageAll, cancellationToken);
        if (deleted && !string.IsNullOrWhiteSpace(uav?.ImageStoragePath))
        {
            await _images.CleanupAsync(uav.ImageStoragePath);
        }

        return deleted;
    }

    // 5. Lấy danh sách UAV đang chờ duyệt ("Pending")
    public async Task<List<UavResponse>> GetPendingAsync(
        string? keyword = null,
        int page = 1,
        int pageSize = 10,
        CancellationToken cancellationToken = default)
    {
        var (safePage, safePageSize) = UavMapper.NormalizePaging(page, pageSize);
        var uavs = await _uavRepository.GetPendingAsync(
            UavMapper.Normalize(keyword),
            safePage,
            safePageSize,
            cancellationToken);

        return uavs
            .Select(x => UavMapper.ToResponse(x, x.Pilot?.User?.FullName ?? string.Empty))
            .ToList();
    }

    // 6. Xem chi tiết UAV
    public async Task<UavResponse?> GetDetailAsync(
        Guid uavId,
        CancellationToken cancellationToken = default)
    {
        var uav = await _uavRepository.GetDetailAsync(uavId, cancellationToken);
        return uav is null
            ? null
            : UavMapper.ToResponse(uav, uav.Pilot?.User?.FullName ?? string.Empty);
    }

    // 7. Quản lý ảnh UAV (ủy quyền hoàn toàn cho UavImageWorkflow)
    public Task<UavResponse> UploadOrReplaceUavImageAsync(
        Guid uavId,
        Guid actorUserId,
        string actorRole,
        Stream fileStream,
        string fileName,
        string contentType,
        long fileSize,
        CancellationToken cancellationToken = default) =>
        _images.UploadOrReplaceAsync(
            uavId, actorUserId, actorRole, fileStream, fileName, contentType, fileSize, cancellationToken);

    public Task<(string Url, string ContentType)?> GetUavImageAsync(
        Guid uavId,
        CancellationToken cancellationToken = default) =>
        _images.GetAsync(uavId, cancellationToken);

    public Task<bool> DeleteUavImageAsync(
        Guid uavId,
        Guid actorUserId,
        string actorRole,
        CancellationToken cancellationToken = default) =>
        _images.DeleteAsync(uavId, actorUserId, actorRole, cancellationToken);

    public Task<UavResponse?> RejectUavImageAsync(
        Guid uavId,
        Guid managerUserId,
        string reason,
        CancellationToken cancellationToken = default) =>
        _images.RejectAsync(uavId, managerUserId, reason, cancellationToken);

    // 8. Chuyển đổi trạng thái vận hành UAV
    public async Task<UavResponse?> RejectAsync(
        Guid uavId,
        CancellationToken cancellationToken = default)
    {
        var uav = await _uavRepository.GetDetailAsync(uavId, cancellationToken);
        if (uav is null)
        {
            return null;
        }

        EnsureStatus(uav, "Pending", "reject");
        uav.Status = "Rejected";

        await _uavRepository.SaveChangesAsync(cancellationToken);
        await _notifications.TryPublishDecisionAsync(uav, false, cancellationToken);

        return UavMapper.ToResponse(uav, uav.Pilot?.User?.FullName ?? string.Empty);
    }

    public async Task<UavResponse?> DeactivateAsync(
        Guid uavId,
        CancellationToken cancellationToken = default)
    {
        var uav = await _uavRepository.GetDetailAsync(uavId, cancellationToken);
        if (uav is null)
        {
            return null;
        }

        EnsureStatus(uav, "Active", "deactivate");
        uav.Status = "Inactive";

        await _uavRepository.SaveChangesAsync(cancellationToken);

        return UavMapper.ToResponse(uav, uav.Pilot?.User?.FullName ?? string.Empty);
    }

    public async Task<UavResponse?> ApproveAsync(
        Guid uavId,
        CancellationToken cancellationToken = default)
    {
        var uav = await _uavRepository.GetDetailAsync(uavId, cancellationToken);
        if (uav is null)
        {
            return null;
        }

        EnsureStatus(uav, "Pending", "approve");
        uav.Status = "Active";

        await _uavRepository.SaveChangesAsync(cancellationToken);
        await _notifications.TryPublishDecisionAsync(uav, true, cancellationToken);

        return UavMapper.ToResponse(uav, uav.Pilot?.User?.FullName ?? string.Empty);
    }

    public async Task<UavResponse?> ActivateAsync(
        Guid uavId,
        CancellationToken cancellationToken = default)
    {
        var uav = await _uavRepository.GetDetailAsync(uavId, cancellationToken);
        if (uav is null)
        {
            return null;
        }

        var currentStatus = uav.Status.Trim();
        var canActivate = currentStatus.Equals("Pending", StringComparison.OrdinalIgnoreCase)
            || currentStatus.Equals("Pending Review", StringComparison.OrdinalIgnoreCase)
            || currentStatus.Equals("Inactive", StringComparison.OrdinalIgnoreCase)
            || currentStatus.Equals("Active", StringComparison.OrdinalIgnoreCase);

        if (!canActivate)
        {
            throw new InvalidOperationException(
                $"Cannot activate UAV with status '{uav.Status}'. Only Pending or Inactive UAVs can be activated.");
        }

        if (!currentStatus.Equals("Active", StringComparison.OrdinalIgnoreCase))
        {
            uav.Status = "Active";
            await _uavRepository.SaveChangesAsync(cancellationToken);
            if (currentStatus.Equals("Pending", StringComparison.OrdinalIgnoreCase) ||
                currentStatus.Equals("Pending Review", StringComparison.OrdinalIgnoreCase))
            {
                await _notifications.TryPublishDecisionAsync(uav, true, cancellationToken);
            }
        }

        return UavMapper.ToResponse(uav, uav.Pilot?.User?.FullName ?? string.Empty);
    }

    // 9. Lấy toàn bộ danh sách UAV có phân trang và lọc theo Status
    public async Task<PagedResponse<UavResponse>> GetAllAsync(
        GetAllUavsRequest request,
        CancellationToken cancellationToken = default)
    {
        var (pageNumber, pageSize) = UavMapper.NormalizePaging(request.PageNumber, request.PageSize);

        var result = await _uavRepository.GetAllPagedAsync(
            UavMapper.Normalize(request.Keyword),
            UavMapper.Normalize(request.Status),
            request.ImageModerationStatus,
            pageNumber,
            pageSize,
            cancellationToken);

        var items = result.Items
            .Select(uav => UavMapper.ToResponse(uav, uav.Pilot?.User?.FullName ?? string.Empty))
            .ToList();

        return UavMapper.CreatePagedResponse(items, result.TotalCount, pageNumber, pageSize);
    }

    private static void ValidateSpecifications(
        decimal? emptyWeightKg,
        decimal? maxTakeoffWeightKg,
        decimal? maxPayloadKg,
        int? batteryCapacityMah,
        decimal? batteryVoltage,
        decimal? batteryMaxVoltage,
        int? batteryCellCount,
        int? maxFlightTimeMinutes,
        decimal? maxRangeKm,
        decimal? maxSpeedKmh,
        decimal? maxWindSpeedMps,
        decimal? maxOperatingAltitudeMeters)
    {
        UavSpecificationValidator.Validate(
            emptyWeightKg,
            maxTakeoffWeightKg,
            maxPayloadKg,
            batteryCapacityMah,
            batteryVoltage,
            batteryCellCount,
            maxFlightTimeMinutes,
            maxRangeKm,
            maxSpeedKmh,
            maxWindSpeedMps,
            maxOperatingAltitudeMeters);

        if (batteryMaxVoltage is <= 0 || batteryMaxVoltage < batteryVoltage)
        {
            throw new ArgumentException(
                "Battery maximum voltage must be positive and at least the nominal voltage when both are known.");
        }
    }

    private static void EnsureStatus(UAV uav, string expectedStatus, string action)
    {
        if (!string.Equals(uav.Status, expectedStatus, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Cannot {action} UAV with status '{uav.Status}'. Only {expectedStatus} UAVs can be {action}d.");
        }
    }
}
