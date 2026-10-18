using DroneOps.Application.DTOs.Request.UAV;
using DroneOps.Application.DTOs.Request.UAVs;
using DroneOps.Application.DTOs.Response.Common;
using DroneOps.Application.DTOs.Response.UAVs;
using DroneOps.Application.Interfaces.UAVs;
using DroneOps.Domain.Entities;
using DroneOps.Persistence.Interfaces;

namespace DroneOps.Application.Services.UAVs;

public sealed class UavService : IUavService
{
    private readonly IUavRepository _uavRepository;
    private readonly IPilotRepository _pilotRepository;

    public UavService(
        IUavRepository uavRepository,
        IPilotRepository pilotRepository)
    {
        _uavRepository = uavRepository;
        _pilotRepository = pilotRepository;
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

        uav.Code = request.UavCode.Trim();
        uav.Name = request.UavName.Trim();
        uav.Model = request.Description?.Trim();

        await _uavRepository.SaveChangesAsync(cancellationToken);
        return true;
    }

    // 4. Xóa UAV: Cho phép xóa toàn bộ Drone ở MỌI TRẠNG THÁI (Pending, Approved, Rejected, Active, Inactive)
    public async Task<bool> DeleteAsync(
        Guid userId,
        Guid uavId,
        CancellationToken cancellationToken = default)
    {
        var uav = await _uavRepository.GetDetailAsync(uavId, cancellationToken);
        if (uav is null)
        {
            return false;
        }

        // Xóa trực tiếp không giới hạn trạng thái Status
        _uavRepository.Remove(uav);
        await _uavRepository.SaveChangesAsync(cancellationToken);
        return true;
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

        uav.Status = "Rejected";

        await _uavRepository.SaveChangesAsync(cancellationToken);

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

        uav.Status = "Inactive";

        await _uavRepository.SaveChangesAsync(cancellationToken);

        return MapToResponse(uav, uav.Pilot?.User?.FullName ?? string.Empty);
    }

    // 9. Duyệt / Kích hoạt UAV -> Chuyển trạng thái sang "Active"
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

        uav.Status = "Active";

        await _uavRepository.SaveChangesAsync(cancellationToken);

        return MapToResponse(uav, uav.Pilot?.User?.FullName ?? string.Empty);
    }

    // 10. Lấy toàn bộ danh sách UAV có phân trang và lọc theo Status
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
            CreatedAt = uav.CreatedAt
        };
    }
}