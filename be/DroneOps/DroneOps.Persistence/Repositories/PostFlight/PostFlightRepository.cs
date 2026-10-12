using DroneOps.Domain.Entities;
using DroneOps.Persistence.Data;
using DroneOps.Persistence.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace DroneOps.Persistence.Repositories;

/// <summary>
/// Repository truy vấn dữ liệu phục vụ phân tích sau bay (SCR-13, SCR-14, UC-17, UC-18, UC-22)
/// </summary>
public class PostFlightRepository : IPostFlightRepository
{
    private readonly AppDbContext _context;

    public PostFlightRepository(AppDbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Lấy danh sách lịch sử bay kèm bộ lọc đa tiêu chí và phân trang
    /// </summary>
    public async Task<(List<Mission> Items, int TotalCount)> GetFlightHistoryAsync(
        int pageNumber,
        int pageSize,
        string? search = null,
        string? status = null,
        Guid? uavId = null,
        DateTime? fromDate = null,
        DateTime? toDate = null,
        Guid? pilotUserId = null,
        CancellationToken cancellationToken = default)
    {
        var query = _context.Missions
            .Include(m => m.UAV)
            .Include(m => m.Creator)
            .AsNoTracking()
            .AsQueryable();

        // Pilot chỉ xem các chuyến bay do chính mình tạo
        if (pilotUserId.HasValue)
        {
            query = query.Where(m => m.CreatedBy == pilotUserId.Value);
        }

        // Lọc theo từ khóa tìm kiếm tên nhiệm vụ
        if (!string.IsNullOrWhiteSpace(search))
        {
            var cleanSearch = search.Trim().ToLower();
            query = query.Where(m => m.Name.ToLower().Contains(cleanSearch));
        }

        // Lọc theo trạng thái
        if (!string.IsNullOrWhiteSpace(status))
        {
            var cleanStatus = status.Trim().ToLower();
            query = query.Where(m => m.Status.ToLower() == cleanStatus);
        }

        // Lọc theo UAV cụ thể
        if (uavId.HasValue)
        {
            query = query.Where(m => m.UAVId == uavId.Value);
        }

        // Lọc theo khoảng thời gian bắt đầu bay
        if (fromDate.HasValue)
        {
            query = query.Where(m => m.StartTime >= fromDate.Value);
        }

        if (toDate.HasValue)
        {
            query = query.Where(m => m.StartTime <= toDate.Value);
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(m => m.StartTime ?? m.CreatedAt)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    /// <summary>
    /// Lấy thông tin cơ bản của nhiệm vụ kèm thông tin UAV và người tạo (không nạp thừa Waypoints kế hoạch)
    /// </summary>
    public async Task<Mission?> GetMissionWithDetailsAsync(Guid missionId, CancellationToken cancellationToken = default)
    {
        return await _context.Missions
            .Include(m => m.UAV)
            .Include(m => m.Creator)
            .FirstOrDefaultAsync(m => m.Id == missionId, cancellationToken);
    }

    /// <summary>
    /// Lấy toàn bộ dữ liệu viễn trắc thực tế theo trình tự thời gian
    /// </summary>
    public async Task<List<TelemetryRecord>> GetTelemetryByMissionIdAsync(Guid missionId, CancellationToken cancellationToken = default)
    {
        return await _context.TelemetryRecords
            .AsNoTracking()
            .Where(t => t.MissionId == missionId)
            .OrderBy(t => t.RecordedAt)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Lấy danh sách các sự cố ghi nhận trong chuyến bay
    /// </summary>
    public async Task<List<Incident>> GetIncidentsByMissionIdAsync(Guid missionId, CancellationToken cancellationToken = default)
    {
        return await _context.Incidents
            .AsNoTracking()
            .Where(i => i.MissionId == missionId)
            .OrderByDescending(i => i.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Đọc báo cáo phân tích sau bay đã tạo
    /// </summary>
    public async Task<PostFlightReport?> GetReportByMissionIdAsync(Guid missionId, CancellationToken cancellationToken = default)
    {
        return await _context.PostFlightReports
            .Include(r => r.Mission)
                .ThenInclude(m => m.UAV)
            .Include(r => r.Mission)
                .ThenInclude(m => m.Creator)
            .FirstOrDefaultAsync(r => r.MissionId == missionId, cancellationToken);
    }

    public async Task AddReportAsync(PostFlightReport report, CancellationToken cancellationToken = default)
    {
        await _context.PostFlightReports.AddAsync(report, cancellationToken);
    }

    public Task UpdateMissionAsync(Mission mission, CancellationToken cancellationToken = default)
    {
        _context.Missions.Update(mission);
        return Task.CompletedTask;
    }

    public async Task<List<PostFlightReport>> GetAllCompletedReportsAsync(Guid? pilotUserId = null, CancellationToken cancellationToken = default)
    {
        var query = _context.PostFlightReports
            .Include(r => r.Mission)
            .AsNoTracking()
            .AsQueryable();

        if (pilotUserId.HasValue)
        {
            query = query.Where(r => r.Mission.CreatedBy == pilotUserId.Value);
        }

        return await query.ToListAsync(cancellationToken);
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        await _context.SaveChangesAsync(cancellationToken);
    }
}