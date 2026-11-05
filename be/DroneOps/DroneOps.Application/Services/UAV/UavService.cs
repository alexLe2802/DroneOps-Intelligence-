using DroneOps.Application.DTOs.Request.UAV;
using DroneOps.Application.DTOs.Request.UAVs;
using DroneOps.Application.DTOs.Response.Common;
using DroneOps.Application.DTOs.Response.UAVs;
using DroneOps.Application.Interfaces.UAVs;
using DroneOps.Application.Interfaces.Notifications;
using DroneOps.Application.Interfaces.Storage;
using DroneOps.Domain.Entities;
using DroneOps.Persistence.Interfaces;
using DroneOps.Application.Validators;
using Microsoft.Extensions.Logging;

namespace DroneOps.Application.Services.UAVs;

public sealed class UavService : IUavService
{
    private readonly IUavRepository _uavRepository;
    private readonly IPilotRepository _pilotRepository;
    private readonly INotificationService _notificationService;
    private readonly ILogger<UavService> _logger;
    private readonly IUserMediaStorage _storage;

    public UavService(
        IUavRepository uavRepository,
        IPilotRepository pilotRepository,
        INotificationService notificationService,
        ILogger<UavService> logger,
        IUserMediaStorage storage,
        IUavImageModerationService imageModeration)
    {
        _uavRepository = uavRepository;
        _pilotRepository = pilotRepository;
        _notificationService = notificationService;
        _logger = logger;
        _storage = storage;
        _ = imageModeration; // Resolved here for configuration validation; moderation runs in the durable API worker.
    }

    // 1. Tạo mới UAV: Trạng thái mặc định là "Pending" (chờ duyệt)
    public async Task<UavResponse> CreateAsync(
    Guid userId,
    CreateUavRequest request,
    CancellationToken cancellationToken = default)
    {
        var pilot = await _pilotRepository.GetByUserIdAsync(
            userId,
            cancellationToken);

        if (pilot is null)
        {
            throw new InvalidOperationException("Pilot profile not found.");
        }

        var code = request.Code.Trim();

        var codeExists = await _uavRepository.CodeExistsAsync(
            code,
            cancellationToken);

        if (codeExists)
        {
            throw new InvalidOperationException("UAV code already exists.");
        }

        // Validate technical specifications
        UavSpecificationValidator.Validate(
            request.EmptyWeightKg,
            request.MaxTakeoffWeightKg,
            request.MaxPayloadKg,
            request.BatteryCapacityMah,
            request.BatteryVoltage,
            request.BatteryCellCount,
            request.MaxFlightTimeMinutes,
            request.MaxRangeKm,
            request.MaxSpeedKmh,
            request.MaxWindSpeedMps,
            request.MaxOperatingAltitudeMeters);
        if (request.BatteryMaxVoltage is <= 0 || request.BatteryMaxVoltage < request.BatteryVoltage)
            throw new ArgumentException("Battery maximum voltage must be positive and at least the nominal voltage when both are known.");

        var uav = new UAV
        {
            Id = Guid.NewGuid(),
            PilotId = pilot.Id,

            // Basic information
            Code = code,
            Name = request.Name.Trim(),
            Manufacturer = Normalize(request.Manufacturer),
            Model = Normalize(request.Model),
            SerialNumber = Normalize(request.SerialNumber),

            // Physical specifications
            EmptyWeightKg = request.EmptyWeightKg,
            MaxTakeoffWeightKg = request.MaxTakeoffWeightKg,
            MaxPayloadKg = request.MaxPayloadKg,

            // Battery specifications
            BatteryType = Normalize(request.BatteryType),
            BatteryCapacityMah = request.BatteryCapacityMah,
            BatteryVoltage = request.BatteryVoltage,
            BatteryMaxVoltage = request.BatteryMaxVoltage,
            BatteryCellCount = request.BatteryCellCount,

            // Flight capabilities
            MaxFlightTimeMinutes = request.MaxFlightTimeMinutes,
            MaxRangeKm = request.MaxRangeKm,
            MaxSpeedKmh = request.MaxSpeedKmh,
            MaxWindSpeedMps = request.MaxWindSpeedMps,
            MaxOperatingAltitudeMeters = request.MaxOperatingAltitudeMeters,

            // Operational information
            PurchaseDate = request.PurchaseDate,

            // System-managed fields
            TotalFlightHours = 0,
            TotalFlights = 0,

            // Must be approved by admin/manager
            Status = "Pending",

            CreatedAt = DateTimeOffset.UtcNow
        };

        await _uavRepository.AddAsync(
            uav,
            cancellationToken);

        await _uavRepository.SaveChangesAsync(
            cancellationToken);

        await TryPublishToManagersAsync(
            "NewPendingUav",
            "New UAV Pending Approval",
            $"UAV '{uav.Code} - {uav.Name}' ({uav.Model ?? "unspecified model"}) has been submitted for approval.",
            userId,
            uav.Id,
            cancellationToken);

        return MapToResponse(
            uav,
            pilot.User?.FullName ?? string.Empty);
    }
    private static string? Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }
    // 2. Lấy danh sách UAV của Pilot đang đăng nhập
    public async Task<PagedResponse<UavResponse>> GetMyUavsAsync(
     Guid userId,
     GetAllUavsRequest request,
     CancellationToken cancellationToken = default)
    {
        var pilot = await _pilotRepository.GetByUserIdAsync(
            userId,
            cancellationToken);

        var pageNumber = request.PageNumber < 1
            ? 1
            : request.PageNumber;

        var pageSize = request.PageSize switch
        {
            < 1 => 10,
            > 100 => 100,
            _ => request.PageSize
        };

        if (pilot is null)
        {
            return new PagedResponse<UavResponse>
            {
                PageNumber = pageNumber,
                PageSize = pageSize,
                TotalItems = 0,
                TotalPages = 0,
                Items = []
            };
        }

        var keyword = string.IsNullOrWhiteSpace(request.Keyword)
            ? null
            : request.Keyword.Trim();

        var status = string.IsNullOrWhiteSpace(request.Status)
            ? null
            : request.Status.Trim();

        var result = await _uavRepository.GetByPilotIdAsync(
            pilot.Id,
            keyword,
            status,
            pageNumber,
            pageSize,
            cancellationToken);

        var items = result.Items
            .Select(x => MapToResponse(
                x,
                x.Pilot?.User?.FullName ??
                pilot.User?.FullName ??
                string.Empty))
            .ToList();

        var totalPages = result.TotalCount == 0
            ? 0
            : (int)Math.Ceiling(
                result.TotalCount / (double)pageSize);

        return new PagedResponse<UavResponse>
        {
            PageNumber = pageNumber,
            PageSize = pageSize,
            TotalItems = result.TotalCount,
            TotalPages = totalPages,
            Items = items
        };
    }

    // 3. Cập nhật thông tin UAV
    public async Task<bool> UpdateAsync(
     Guid userId,
     Guid uavId,
     UpdateUavRequest request,
     CancellationToken cancellationToken = default)
    {
        var uav = await _uavRepository.GetDetailAsync(
            uavId,
            cancellationToken);

        if (uav is null)
        {
            return false;
        }

        // Pilot chỉ được update UAV của mình
        if (uav.Pilot.UserId != userId)
        {
            return false;
        }

        var newCode = request.Code.Trim();

        // Nếu đổi Code thì mới cần check duplicate
        if (!string.Equals(
            uav.Code,
            newCode,
            StringComparison.OrdinalIgnoreCase))
        {
            var codeExists = await _uavRepository.CodeExistsAsync(
                newCode,
                cancellationToken);

            if (codeExists)
            {
                throw new InvalidOperationException(
                    "UAV code already exists.");
            }
        }

        // Dùng chung validator với Create
        UavSpecificationValidator.Validate(
            request.EmptyWeightKg,
            request.MaxTakeoffWeightKg,
            request.MaxPayloadKg,
            request.BatteryCapacityMah,
            request.BatteryVoltage,
            request.BatteryCellCount,
            request.MaxFlightTimeMinutes,
            request.MaxRangeKm,
            request.MaxSpeedKmh,
            request.MaxWindSpeedMps,
            request.MaxOperatingAltitudeMeters);
        if (request.BatteryMaxVoltage is <= 0 || request.BatteryMaxVoltage < request.BatteryVoltage)
            throw new ArgumentException("Battery maximum voltage must be positive and at least the nominal voltage when both are known.");

        // Basic information
        uav.Code = newCode;
        uav.Name = request.Name.Trim();
        uav.Manufacturer = Normalize(request.Manufacturer);
        uav.Model = Normalize(request.Model);
        uav.SerialNumber = Normalize(request.SerialNumber);

        // Physical
        uav.EmptyWeightKg = request.EmptyWeightKg;
        uav.MaxTakeoffWeightKg = request.MaxTakeoffWeightKg;
        uav.MaxPayloadKg = request.MaxPayloadKg;

        // Battery
        uav.BatteryType = Normalize(request.BatteryType);
        uav.BatteryCapacityMah = request.BatteryCapacityMah;
        uav.BatteryVoltage = request.BatteryVoltage;
        uav.BatteryMaxVoltage = request.BatteryMaxVoltage;
        uav.BatteryCellCount = request.BatteryCellCount;

        // Flight capability
        uav.MaxFlightTimeMinutes = request.MaxFlightTimeMinutes;
        uav.MaxRangeKm = request.MaxRangeKm;
        uav.MaxSpeedKmh = request.MaxSpeedKmh;
        uav.MaxWindSpeedMps = request.MaxWindSpeedMps;
        uav.MaxOperatingAltitudeMeters =
            request.MaxOperatingAltitudeMeters;

        uav.PurchaseDate = request.PurchaseDate;

        // Pilot thay đổi thông tin UAV => cần duyệt lại
        uav.Status = "Pending";

        await _uavRepository.SaveChangesAsync(
            cancellationToken);

        await TryPublishToManagersAsync(
            "UavUpdatePending",
            "UAV Update Pending Approval",
            $"UAV '{uav.Code} - {uav.Name}' has been updated and requires approval.",
            userId,
            uav.Id,
            cancellationToken);

        return true;
    }

    // 4. Xóa UAV chưa gắn flight controller. Binding lâu dài không được
    // xóa lách qua API UAV; UAV đã bind chỉ có thể deactivate.
    public async Task<bool> DeleteAsync(
        Guid userId,
        Guid uavId,
        bool canManageAll = false,
        CancellationToken cancellationToken = default)
    {
        var uav = await _uavRepository.GetDetailAsync(uavId, cancellationToken);
        var deleted = await _uavRepository.DeleteOwnedAsync(
            uavId,
            userId,
            canManageAll,
            cancellationToken);
        if (deleted && !string.IsNullOrWhiteSpace(uav?.ImageStoragePath))
        {
            await CleanupImageAsync(uav.ImageStoragePath);
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
        var uavs = await _uavRepository.GetPendingAsync(
            keyword,
            page,
            pageSize,
            cancellationToken);

        return uavs
            .Select(x => MapToResponse(x, x.Pilot?.User?.FullName ?? string.Empty))
            .ToList();
    }

    // 6. Xem chi tiết UAV
    public async Task<UavResponse?> GetDetailAsync(
        Guid uavId,
        CancellationToken cancellationToken = default)
    {
        var uav = await _uavRepository.GetDetailAsync(
            uavId,
            cancellationToken);

        if (uav is null)
        {
            return null;
        }

        return MapToResponse(uav, uav.Pilot?.User?.FullName ?? string.Empty);
    }

    public async Task<UavResponse> UploadOrReplaceUavImageAsync(
    Guid uavId,
    Guid actorUserId,
    string actorRole,
    Stream fileStream,
    string fileName,
    string contentType,
    long fileSize,
    CancellationToken cancellationToken = default)
    {
        var uav = await _uavRepository.GetDetailAsync(
            uavId,
            cancellationToken)
            ?? throw new KeyNotFoundException("UAV not found.");

        EnsureImagePermission(
            uav,
            actorUserId,
            actorRole);

        var normalizedContentType = contentType
            .Trim()
            .ToLowerInvariant();

        var imageBytes = await _storage.ReadValidatedUavImageAsync(
            fileStream,
            fileName,
            normalizedContentType,
            fileSize,
            cancellationToken);

        var extension = normalizedContentType switch
        {
            "image/jpeg" => ".jpg",
            "image/png" => ".png",
            "image/webp" => ".webp",
            _ => throw new ArgumentException(
                "Unsupported UAV image type.")
        };

        var newPath =
            $"uavs/{uav.Id:N}/{Guid.NewGuid():N}{extension}";

        // Upload ảnh mới trước
        await using (var uploadStream =
            new MemoryStream(imageBytes, writable: false))
        {
            await _storage.UploadAsync(
                newPath,
                uploadStream,
                normalizedContentType,
                cancellationToken);
        }

        var oldPath = uav.ImageStoragePath;

        // Update UAV
        uav.ImageStoragePath = newPath;
        uav.ImageContentType = normalizedContentType;
        uav.ImageUpdatedAt = DateTimeOffset.UtcNow;
        uav.ImageModerationStatus = "Pending";
        uav.ImageModerationAttempts = 0;
        uav.ImageModerationNextAttemptAt = DateTimeOffset.UtcNow;
        uav.ImageModeratedAt = null;
        uav.ImageModerationReason = "Waiting for AI moderation.";

        // Bất kỳ thay đổi nào của UAV cũng cần duyệt lại
        uav.Status = "Pending";

        try
        {
            await _uavRepository.SaveChangesAsync(
                cancellationToken);
        }
        catch
        {
            // DB save fail thì xóa ảnh mới vừa upload
            await CleanupImageAsync(newPath);
            throw;
        }

        // DB đã lưu path mới thành công thì mới xóa ảnh cũ
        if (!string.IsNullOrWhiteSpace(oldPath) &&
            !string.Equals(
                oldPath,
                newPath,
                StringComparison.Ordinal))
        {
            await CleanupImageAsync(oldPath);
        }

        // Thông báo cho manager/admin rằng UAV cần được duyệt lại
        await TryPublishToManagersAsync(
            "UavUpdatePending",
            "UAV Update Pending Approval",
            $"UAV '{uav.Code} - {uav.Name}' image has been updated and requires approval.",
            actorUserId,
            uav.Id,
            cancellationToken);

        return MapToResponse(
            uav,
            uav.Pilot?.User?.FullName ?? string.Empty);
    }

    public async Task<(string Url, string ContentType)?> GetUavImageAsync(
        Guid uavId,
        CancellationToken cancellationToken = default)
    {
        var uav = await _uavRepository.GetDetailAsync(uavId, cancellationToken);
        if (uav is null || string.IsNullOrWhiteSpace(uav.ImageStoragePath)) return null;
        return (await _storage.SignAsync(uav.ImageStoragePath, cancellationToken),
            uav.ImageContentType ?? "application/octet-stream");
    }

    public async Task<bool> DeleteUavImageAsync(
    Guid uavId,
    Guid actorUserId,
    string actorRole,
    CancellationToken cancellationToken = default)
    {
        var uav = await _uavRepository.GetDetailAsync(
            uavId,
            cancellationToken);

        if (uav is null)
        {
            return false;
        }

        // Check pilot/manager có quyền thao tác UAV này
        EnsureImagePermission(
            uav,
            actorUserId,
            actorRole);

        var oldPath = uav.ImageStoragePath;

        // UAV vốn không có ảnh -> không có gì thay đổi
        if (string.IsNullOrWhiteSpace(oldPath))
        {
            return true;
        }

        // Remove image information
        uav.ImageStoragePath = null;
        uav.ImageContentType = null;
        uav.ImageUpdatedAt = DateTimeOffset.UtcNow;
        uav.ImageModerationStatus = null;
        uav.ImageModerationAttempts = 0;
        uav.ImageModerationNextAttemptAt = null;
        uav.ImageModeratedAt = null;
        uav.ImageModerationReason = null;

        // Có thay đổi thông tin UAV -> cần duyệt lại
        uav.Status = "Pending";

        // Save DB trước
        await _uavRepository.SaveChangesAsync(
            cancellationToken);

        // DB thành công rồi mới xóa file trên storage
        await CleanupImageAsync(oldPath);

        // Notify manager/admin để duyệt lại
        await TryPublishToManagersAsync(
            "UavUpdatePending",
            "UAV Update Pending Approval",
            $"UAV '{uav.Code} - {uav.Name}' image has been removed and requires approval.",
            actorUserId,
            uav.Id,
            cancellationToken);

        return true;
    }

    // 7. Từ chối duyệt UAV -> Chuyển trạng thái sang "Rejected"
    public async Task<UavResponse?> RejectAsync(
        Guid uavId,
        CancellationToken cancellationToken = default)
    {
        var uav = await _uavRepository.GetDetailAsync(
            uavId,
            cancellationToken);

        if (uav is null)
        {
            return null;
        }

        EnsureStatus(uav, "Pending", "reject");
        uav.Status = "Rejected";

        await _uavRepository.SaveChangesAsync(cancellationToken);
        await TryPublishDecisionAsync(uav, false, cancellationToken);

        return MapToResponse(uav, uav.Pilot?.User?.FullName ?? string.Empty);
    }

    // 8. Vô hiệu hóa UAV -> Chuyển trạng thái sang "Inactive"
    public async Task<UavResponse?> DeactivateAsync(
        Guid uavId,
        CancellationToken cancellationToken = default)
    {
        var uav = await _uavRepository.GetDetailAsync(
            uavId,
            cancellationToken);

        if (uav is null)
        {
            return null;
        }

        EnsureStatus(uav, "Active", "deactivate");
        uav.Status = "Inactive";

        await _uavRepository.SaveChangesAsync(cancellationToken);

        return MapToResponse(uav, uav.Pilot?.User?.FullName ?? string.Empty);
    }

    // 9. Duyệt UAV: Pending -> Active. "Approved" là hành động, không phải trạng thái UAV.
    public async Task<UavResponse?> ApproveAsync(
        Guid uavId,
        CancellationToken cancellationToken = default)
    {
        var uav = await _uavRepository.GetDetailAsync(
            uavId,
            cancellationToken);

        if (uav is null)
        {
            return null;
        }

        EnsureStatus(uav, "Pending", "approve");
        EnsureImageModerationAllowsApproval(uav);
        uav.Status = "Active";

        await _uavRepository.SaveChangesAsync(cancellationToken);
        await TryPublishDecisionAsync(uav, true, cancellationToken);

        return MapToResponse(uav, uav.Pilot?.User?.FullName ?? string.Empty);
    }

    // 10. Duyệt/kích hoạt UAV theo nút "Approve & Activate" của giao diện:
    // Pending/Pending Review -> Active; Inactive -> Active; Active -> Active (retry an toàn).
    public async Task<UavResponse?> ActivateAsync(
        Guid uavId,
        CancellationToken cancellationToken = default)
    {
        var uav = await _uavRepository.GetDetailAsync(
            uavId,
            cancellationToken);

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

        EnsureImageModerationAllowsApproval(uav);

        if (!currentStatus.Equals("Active", StringComparison.OrdinalIgnoreCase))
        {
            uav.Status = "Active";
            await _uavRepository.SaveChangesAsync(cancellationToken);
            if (currentStatus.Equals("Pending", StringComparison.OrdinalIgnoreCase) ||
                currentStatus.Equals("Pending Review", StringComparison.OrdinalIgnoreCase))
            {
                await TryPublishDecisionAsync(uav, true, cancellationToken);
            }
        }

        return MapToResponse(uav, uav.Pilot?.User?.FullName ?? string.Empty);
    }

    // 11. Lấy toàn bộ danh sách UAV có phân trang và lọc theo Status
    public async Task<PagedResponse<UavResponse>> GetAllAsync(
        GetAllUavsRequest request,
        CancellationToken cancellationToken = default)
    {
        int pageNumber = request.PageNumber < 1 ? 1 : request.PageNumber;

        int pageSize = request.PageSize switch
        {
            < 1 => 10,
            > 100 => 100,
            _ => request.PageSize
        };

        string? keyword = string.IsNullOrWhiteSpace(request.Keyword)
            ? null
            : request.Keyword.Trim();

        string? status = string.IsNullOrWhiteSpace(request.Status)
            ? null
            : request.Status.Trim();

        (IReadOnlyList<UAV> Items, int TotalCount) result =
            await _uavRepository.GetAllPagedAsync(
                keyword,
                status,
                pageNumber,
                pageSize,
                cancellationToken);

        IReadOnlyList<UavResponse> items = result.Items
            .Select(uav => MapToResponse(uav, uav.Pilot?.User?.FullName ?? string.Empty))
            .ToList();

        int totalPages = result.TotalCount == 0
            ? 0
            : (int)Math.Ceiling(result.TotalCount / (double)pageSize);

        return new PagedResponse<UavResponse>
        {
            PageNumber = pageNumber,
            PageSize = pageSize,
            TotalItems = result.TotalCount,
            TotalPages = totalPages,
            Items = items
        };
    }

   

    private static void EnsureStatus(UAV uav, string expectedStatus, string action)
    {
        if (!string.Equals(uav.Status, expectedStatus, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Cannot {action} UAV with status '{uav.Status}'. Only {expectedStatus} UAVs can be {action}d.");
        }
    }

    private static void EnsureImageModerationAllowsApproval(UAV uav)
    {
        if (string.IsNullOrWhiteSpace(uav.ImageStoragePath)) return;

        var status = uav.ImageModerationStatus?.Trim();
        if (string.Equals(status, "Approved", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(status, "ManualReview", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (string.Equals(status, "Rejected", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "The UAV image was rejected by moderation. Replace it before approving the UAV.");
        }

        throw new InvalidOperationException(
            "The UAV image is still waiting for AI moderation. Retry approval after the image check completes.");
    }

    private static void EnsureImagePermission(UAV uav, Guid actorUserId, string actorRole)
    {
        var normalizedRole = actorRole.Trim().ToLowerInvariant();
        var isManager = normalizedRole is "operation_manager" or "operations_manager" or
            "operationsmanager" or "admin" or "systemadmin";
        if (!isManager && uav.Pilot.UserId != actorUserId)
        {
            throw new UnauthorizedAccessException("You may only manage the image of your own UAV.");
        }
    }

    private async Task CleanupImageAsync(string path)
    {
        try
        {
            await _storage.DeleteAsync([path], CancellationToken.None);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Unable to remove replaced UAV image {ImagePath}.", path);
        }
    }

    private async Task TryPublishToManagersAsync(
        string type, string title, string message, Guid actorUserId, Guid uavId,
        CancellationToken cancellationToken)
    {
        try
        {
            await _notificationService.PublishToManagersAsync(type, title, message, actorUserId,
                uavId, "UAV", $"/uav-management?uavId={uavId}", cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(exception, "Could not publish UAV notification for {UavId}.", uavId);
        }
    }

    private async Task TryPublishDecisionAsync(UAV uav, bool approved, CancellationToken cancellationToken)
    {
        try
        {
            var recipientUserId = uav.Pilot.UserId;
            var type = approved ? "UavApproved" : "UavRejected";
            var title = approved ? "UAV Approved" : "UAV Rejected";
            var message = approved
                ? $"Your UAV '{uav.Code} - {uav.Name}' has been approved and is now operational."
                : $"Your UAV '{uav.Code} - {uav.Name}' request was rejected.";
            await _notificationService.PublishToUserAsync(recipientUserId, type, title, message, null,
                uav.Id, "UAV", $"/uav-management?uavId={uav.Id}", cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(exception, "Could not publish UAV decision notification for {UavId}.", uav.Id);
        }
    }
    private static UavResponse MapToResponse(UAV uav, string pilotName)
    {
        return new UavResponse
        {
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
            FirmwareNavigationDefaults = uav.FirmwareNavigationDefaultsJson == null ? null : System.Text.Json.JsonSerializer.Deserialize<FirmwareParameterSnapshot>(uav.FirmwareNavigationDefaultsJson, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)),
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
