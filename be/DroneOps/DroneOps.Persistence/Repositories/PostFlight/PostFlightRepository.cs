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
            .Where(t => t.MissionId == missionId && t.IsValid)
            .OrderBy(t => t.SourceTimestamp)
            .ThenBy(t => t.ReceivedAt)
            .ToListAsync(cancellationToken);
    }

    public Task<FlightCompletionEvent?> GetCompletionEventByKeyAsync(
        string idempotencyKey, CancellationToken cancellationToken = default) =>
        _context.FlightCompletionEvents.AsNoTracking()
            .FirstOrDefaultAsync(x => x.IdempotencyKey == idempotencyKey, cancellationToken);

    public Task<FlightCompletionEvent?> GetTrustedCompletionEventAsync(
        Guid missionId, CancellationToken cancellationToken = default) =>
        _context.FlightCompletionEvents.AsNoTracking()
            .Where(x => x.MissionId == missionId && x.IsTrusted)
            .OrderByDescending(x => x.CompletedAt)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task AddCompletionEventAsync(
        FlightCompletionEvent completionEvent, CancellationToken cancellationToken = default) =>
        await _context.FlightCompletionEvents.AddAsync(completionEvent, cancellationToken);

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
            .FirstOrDefaultAsync(r => r.MissionId == missionId && r.IsCurrent, cancellationToken);
    }

    public Task<List<PostFlightReport>> GetReportRevisionsAsync(
        Guid missionId, CancellationToken cancellationToken = default) =>
        _context.PostFlightReports.AsNoTracking()
            .Where(x => x.MissionId == missionId)
            .OrderByDescending(x => x.Revision)
            .ToListAsync(cancellationToken);

    public Task<List<PostFlightReport>> GetReportsAwaitingGenerationAsync(
        int batchSize, CancellationToken cancellationToken = default)
    {
        var abandonedBefore = DateTimeOffset.UtcNow.AddMinutes(-5);
        return _context.PostFlightReports
            .Include(x => x.Mission).ThenInclude(x => x.UAV)
            .Include(x => x.Mission).ThenInclude(x => x.Creator)
            .Where(x => x.IsCurrent &&
                (x.GenerationStatus == "Queued" ||
                 (x.GenerationStatus == "Generating" && x.GenerationStartedAt < abandonedBefore)))
            .OrderBy(x => x.CreatedAt)
            .Take(batchSize)
            .ToListAsync(cancellationToken);
    }

    public async Task AddReportAsync(PostFlightReport report, CancellationToken cancellationToken = default)
    {
        await _context.PostFlightReports.AddAsync(report, cancellationToken);
    }

    public async Task ReplaceCurrentReportAsync(
        PostFlightReport currentReport,
        PostFlightReport nextReport,
        CancellationToken cancellationToken = default)
    {
        if (!_context.Database.IsRelational())
        {
            currentReport.IsCurrent = false;
            await _context.PostFlightReports.AddAsync(nextReport, cancellationToken);
            await _context.SaveChangesAsync(cancellationToken);
            return;
        }

        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        currentReport.IsCurrent = false;
        await _context.SaveChangesAsync(cancellationToken);
        await _context.PostFlightReports.AddAsync(nextReport, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public Task UpdateMissionAsync(Mission mission, CancellationToken cancellationToken = default)
    {
        _context.Missions.Update(mission);
        return Task.CompletedTask;
    }

    public async Task<List<Mission>> GetTerminalMissionsAsync(
        DateTime? fromDate, DateTime? toDate, Guid? uavId, Guid? missionId,
        Guid? pilotUserId, CancellationToken cancellationToken = default)
    {
        var terminalStatuses = new[] { "Completed", "Failed", "Aborted", "Cancelled" };
        var query = _context.Missions
            .Include(x => x.UAV)
            .AsNoTracking()
            .Where(x => terminalStatuses.Contains(x.Status));

        if (pilotUserId.HasValue)
            query = query.Where(x => x.CreatedBy == pilotUserId.Value);
        if (fromDate.HasValue)
            query = query.Where(x => (x.EndTime ?? x.StartTime ?? x.CreatedAt) >= fromDate.Value);
        if (toDate.HasValue)
            query = query.Where(x => (x.EndTime ?? x.StartTime ?? x.CreatedAt) <= toDate.Value);
        if (uavId.HasValue)
            query = query.Where(x => x.UAVId == uavId.Value);
        if (missionId.HasValue)
            query = query.Where(x => x.Id == missionId.Value);

        return await query.ToListAsync(cancellationToken);
    }

    public async Task MarkReportsOutdatedForLateTelemetryAsync(
        CancellationToken cancellationToken = default)
    {
        var candidates = await _context.PostFlightReports
            .Where(report => report.IsCurrent && report.GenerationStatus == "Available" && report.GeneratedAt != null)
            .Where(report => _context.TelemetryRecords.Any(record =>
                record.MissionId == report.MissionId && record.IsValid &&
                record.ReceivedAt > report.GeneratedAt &&
                (!report.CoveredTelemetryThrough.HasValue || record.SourceTimestamp <= report.CoveredTelemetryThrough)))
            .ToListAsync(cancellationToken);

        foreach (var report in candidates)
            report.GenerationStatus = "Outdated";

        if (candidates.Count > 0)
            await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        await _context.SaveChangesAsync(cancellationToken);
    }
}
