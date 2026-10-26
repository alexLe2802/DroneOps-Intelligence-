using DroneOps.Application.DTOs.Request.UAV;
using DroneOps.Application.DTOs.Request.UAVs;
using DroneOps.Application.DTOs.Response.Common;
using DroneOps.Application.DTOs.Response.UAVs;
using DroneOps.Application.Interfaces.UAVs;
using DroneOps.Application.Interfaces.Notifications;
using DroneOps.Application.Interfaces.Storage;
using DroneOps.Domain.Entities;
using DroneOps.Persistence.Interfaces;
using Microsoft.Extensions.Logging;

namespace DroneOps.Application.Services.UAVs;

public sealed class UavService : IUavService
{
    private readonly IUavRepository _uavRepository;
    private readonly IPilotRepository _pilotRepository;
    private readonly INotificationService _notificationService;
    private readonly ILogger<UavService> _logger;
    private readonly IUserMediaStorage _storage;
    private readonly IUavImageModerationService _imageModeration;

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
        _imageModeration = imageModeration;
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

        var codeExists = await _uavRepository.CodeExistsAsync(
            request.Code.Trim(),
            cancellationToken);

        if (codeExists)
        {
            throw new InvalidOperationException("UAV code already exists.");
        }

        var uav = new UAV
        {
            Id = Guid.NewGuid(),
            PilotId = pilot.Id,
            Code = request.Code.Trim(),
            Name = request.Name.Trim(),
            Model = request.Model?.Trim(),
            Status = "Pending", // Đã đồng bộ thành "Pending" thay vì "Pending Review"
            CreatedAt = DateTime.UtcNow
        };

        await _uavRepository.AddAsync(uav, cancellationToken);
        await _uavRepository.SaveChangesAsync(cancellationToken);
        await TryPublishToManagersAsync(
            "NewPendingUav", "New UAV Pending Approval",
            $"UAV '{uav.Code} - {uav.Name}' ({uav.Model ?? "unspecified model"}) has been submitted for approval.",
            userId, uav.Id, cancellationToken);

        return MapToResponse(uav, pilot.User?.FullName ?? string.Empty);
    }

    // 2. Lấy danh sách UAV của Pilot đang đăng nhập
    public async Task<List<UavResponse>> GetMyUavsAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var pilot = await _pilotRepository.GetByUserIdAsync(
            userId,
            cancellationToken);

        if (pilot is null)
        {
            return [];
        }

        var uavs = await _uavRepository.GetByPilotIdAsync(
            pilot.Id,
            cancellationToken);

        return uavs
            .Select(x => MapToResponse(x, pilot.User?.FullName ?? string.Empty))
            .ToList();
    }

    // 3. Cập nhật thông tin UAV
    public async Task<bool> UpdateAsync(
        Guid userId,
        Guid uavId,
        UpdateUavRequest request,
        CancellationToken cancellationToken = default)
    {
        var uav = await _uavRepository.GetDetailAsync(uavId, cancellationToken);
        if (uav is null)
        {
            return false;
        }

        uav.Code = request.Code.Trim();
        uav.Name = request.Name.Trim();
        uav.Model = string.IsNullOrWhiteSpace(request.Model)
            ? null
            : request.Model.Trim();

        await _uavRepository.SaveChangesAsync(cancellationToken);
        return true;
    }

    // 4. Xóa UAV: Cho phép xóa toàn bộ Drone ở MỌI TRẠNG THÁI (Pending, Rejected, Active, Inactive)
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
        var uav = await _uavRepository.GetDetailAsync(uavId, cancellationToken)
            ?? throw new KeyNotFoundException("UAV not found.");
        EnsureImagePermission(uav, actorUserId, actorRole);

        var normalizedContentType = contentType.Trim().ToLowerInvariant();
        var imageBytes = await _storage.ReadValidatedUavImageAsync(
            fileStream, fileName, normalizedContentType, fileSize, cancellationToken);
        if (!await _imageModeration.IsAllowedAsync(imageBytes, normalizedContentType, cancellationToken))
        {
            throw new ArgumentException(
                "Invalid image content: The uploaded photo must be a valid UAV/Drone image and must not contain inappropriate content.");
        }

        var extension = normalizedContentType switch
        {
            "image/jpeg" => ".jpg",
            "image/png" => ".png",
            "image/webp" => ".webp",
            _ => throw new ArgumentException("Unsupported UAV image type.")
        };
        var newPath = $"uavs/{uav.Id:N}/{Guid.NewGuid():N}{extension}";
        await using (var uploadStream = new MemoryStream(imageBytes, writable: false))
        {
            await _storage.UploadAsync(newPath, uploadStream, normalizedContentType, cancellationToken);
        }

        var oldPath = uav.ImageStoragePath;
        uav.ImageStoragePath = newPath;
        uav.ImageContentType = normalizedContentType;
        uav.ImageUpdatedAt = DateTimeOffset.UtcNow;
        try
        {
            await _uavRepository.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            await CleanupImageAsync(newPath);
            throw;
        }

        if (!string.IsNullOrWhiteSpace(oldPath) && !string.Equals(oldPath, newPath, StringComparison.Ordinal))
        {
            await CleanupImageAsync(oldPath);
        }

        return MapToResponse(uav, uav.Pilot?.User?.FullName ?? string.Empty);
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
        var uav = await _uavRepository.GetDetailAsync(uavId, cancellationToken);
        if (uav is null) return false;
        EnsureImagePermission(uav, actorUserId, actorRole);
        var oldPath = uav.ImageStoragePath;
        if (string.IsNullOrWhiteSpace(oldPath)) return true;

        uav.ImageStoragePath = null;
        uav.ImageContentType = null;
        uav.ImageUpdatedAt = DateTimeOffset.UtcNow;
        await _uavRepository.SaveChangesAsync(cancellationToken);
        await CleanupImageAsync(oldPath);
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

    private static UavResponse MapToResponse(UAV uav, string pilotName)
    {
        return new UavResponse
        {
            Id = uav.Id,
            PilotId = uav.PilotId,
            PilotName = pilotName,
            Code = uav.Code,
            Name = uav.Name,
            Model = uav.Model,
            Status = uav.Status,
            ImageUrl = string.IsNullOrWhiteSpace(uav.ImageStoragePath)
                ? null
                : $"/api/uavs/{uav.Id}/image?v={Uri.EscapeDataString(uav.ImageStoragePath)}",
            CreatedAt = uav.CreatedAt
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
}
