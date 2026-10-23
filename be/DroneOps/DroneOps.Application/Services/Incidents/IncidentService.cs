using System.Text.Json;
using DroneOps.Application.DTOs.Request.Incidents;
using DroneOps.Application.DTOs.Response.Incidents;
using DroneOps.Application.Interfaces.Incidents;
using DroneOps.Domain.Entities;
using DroneOps.Persistence.Data;
using Microsoft.EntityFrameworkCore;

namespace DroneOps.Application.Services.Incidents;

public class IncidentService : IIncidentService
{
    private readonly AppDbContext _context;
    private readonly IIncidentLifecycleService _lifecycle;

    public IncidentService(AppDbContext context, IIncidentLifecycleService lifecycle)
    {
        _context = context;
        _lifecycle = lifecycle;
    }

    public async Task<PagedIncidentsResponse> GetIncidentsAsync(
        GetIncidentsRequest request,
        Guid? currentUserId,
        bool isManager,
        CancellationToken cancellationToken = default)
    {
        var query = _context.Incidents
            .AsNoTracking()
            .AsQueryable();

        // Pilot / UAV Operator chỉ được xem các sự cố thuộc mission do chính mình quản lý
        if (!isManager && !currentUserId.HasValue)
        {
            throw new UnauthorizedAccessException("Unable to verify pilot identity.");
        }

        if (!isManager)
        {
            query = query.Where(i => i.Mission != null && i.Mission.CreatedBy == currentUserId!.Value);
        }

        // Lọc theo Mission cụ thể
        if (request.MissionId.HasValue)
        {
            query = query.Where(i => i.MissionId == request.MissionId.Value);
        }

        // Lọc theo UAV cụ thể
        if (request.UavId.HasValue)
        {
            query = query.Where(i => i.Mission != null && i.Mission.UAVId == request.UavId.Value);
        }

        // Lọc theo Loại sự cố (Type)
        if (!string.IsNullOrWhiteSpace(request.Type))
        {
            var cleanType = request.Type.Trim().ToLower();
            query = query.Where(i => i.Type.ToLower() == cleanType);
        }

        // Lọc theo Mức độ nghiêm trọng (Severity)
        if (!string.IsNullOrWhiteSpace(request.Severity))
        {
            var cleanSev = request.Severity.Trim().ToLower();
            query = query.Where(i => i.Severity != null && i.Severity.ToLower() == cleanSev);
        }

        // Lọc theo Trạng thái (Status: Open, Acknowledged, Resolved)
        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            var cleanStatus = request.Status.Trim().ToLower();
            query = query.Where(i => i.Status.ToLower() == cleanStatus);
        }

        // Tìm kiếm theo từ khóa trong Title hoặc Description
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var cleanSearch = request.Search.Trim().ToLower();
            query = query.Where(i =>
                i.Title.ToLower().Contains(cleanSearch) ||
                (i.Description != null && i.Description.ToLower().Contains(cleanSearch)));
        }

        // Lọc theo khoảng thời gian
        if (request.FromDate.HasValue)
        {
            query = query.Where(i => i.CreatedAt >= request.FromDate.Value);
        }

        if (request.ToDate.HasValue)
        {
            query = query.Where(i => i.CreatedAt <= request.ToDate.Value);
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var pageNumber = request.PageNumber <= 0 ? 1 : request.PageNumber;
        var pageSize = request.PageSize <= 0 ? 10 : (request.PageSize > 100 ? 100 : request.PageSize);

        var items = await query
            .OrderByDescending(i => i.CreatedAt)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .Select(i => new IncidentItemResponse
            {
                Id = i.Id,
                MissionId = i.MissionId,
                MissionName = i.Mission != null ? i.Mission.Name : string.Empty,
                UavId = i.Mission != null ? i.Mission.UAVId : null,
                UavCode = i.Mission != null && i.Mission.UAV != null ? i.Mission.UAV.Code : null,
                UavName = i.Mission != null && i.Mission.UAV != null ? i.Mission.UAV.Name : null,
                Title = i.Title,
                Description = i.Description,
                Type = i.Type,
                Severity = i.Severity ?? "Medium",
                Status = i.Status,
                Latitude = i.Latitude,
                Longitude = i.Longitude,
                CreatedAt = i.CreatedAt,
                AcknowledgedAt = i.AcknowledgedAt,
                ResolvedAt = i.ResolvedAt
            })
            .ToListAsync(cancellationToken);

        return new PagedIncidentsResponse
        {
            Items = items,
            TotalCount = totalCount,
            PageNumber = pageNumber,
            PageSize = pageSize
        };
    }

    public async Task<IncidentDetailResponse?> GetIncidentByIdAsync(
        Guid incidentId,
        Guid? currentUserId,
        bool isManager,
        CancellationToken cancellationToken = default)
    {
        var incident = await _context.Incidents
            .AsNoTracking()
            .Include(i => i.Mission)
                .ThenInclude(m => m.UAV)
            .Include(i => i.Acknowledger)
            .Include(i => i.Resolver)
            .FirstOrDefaultAsync(i => i.Id == incidentId, cancellationToken);

        if (incident == null) return null;

        // Kiểm tra phân quyền truy cập
        if (!isManager && (!currentUserId.HasValue || incident.Mission.CreatedBy != currentUserId.Value))
        {
            throw new UnauthorizedAccessException("You do not have permission to view this incident.");
        }

        var evidence = ParseEvidence(incident.Evidence);

        return new IncidentDetailResponse
        {
            Id = incident.Id,
            MissionId = incident.MissionId,
            MissionName = incident.Mission.Name,
            UavId = incident.Mission.UAVId,
            UavCode = incident.Mission.UAV?.Code,
            UavName = incident.Mission.UAV?.Name,
            Title = incident.Title,
            Description = incident.Description,
            Type = incident.Type,
            Severity = incident.Severity ?? "Medium",
            Status = incident.Status,
            Latitude = incident.Latitude,
            Longitude = incident.Longitude,
            CreatedAt = incident.CreatedAt,
            Evidence = evidence.Payload,
            AcknowledgedAt = incident.AcknowledgedAt,
            AcknowledgedBy = incident.AcknowledgedBy,
            AcknowledgedByName = incident.Acknowledger?.FullName ?? incident.Acknowledger?.Email,
            ResolvedAt = incident.ResolvedAt,
            ResolvedBy = incident.ResolvedBy,
            ResolvedByName = incident.Resolver?.FullName ?? incident.Resolver?.Email,
            ResolutionNotes = incident.ResolutionNotes,
            SourceTimestamp = evidence.SourceTimestamp,
            ReceivedAt = evidence.ReceivedAt,
            AppliedRule = evidence.AppliedRule,
            Threshold = evidence.Threshold
        };
    }

    public async Task<List<IncidentItemResponse>> GetMissionIncidentFeedAsync(
        Guid missionId,
        Guid? currentUserId,
        bool isManager,
        DateTimeOffset? changedAfter,
        CancellationToken cancellationToken = default)
    {
        var query = _context.Incidents
            .AsNoTracking()
            .Where(i => i.MissionId == missionId);

        if (!isManager && !currentUserId.HasValue)
        {
            throw new UnauthorizedAccessException("Unable to verify pilot identity.");
        }

        if (!isManager)
        {
            query = query.Where(i => i.Mission.CreatedBy == currentUserId!.Value);
        }

        if (changedAfter.HasValue)
        {
            var since = changedAfter.Value;
            query = query.Where(i =>
                i.CreatedAt > since ||
                (i.AcknowledgedAt != null && i.AcknowledgedAt > since) ||
                (i.ResolvedAt != null && i.ResolvedAt > since));
        }

        return await query
            .OrderBy(i => i.CreatedAt)
            .Select(i => new IncidentItemResponse
            {
                Id = i.Id,
                MissionId = i.MissionId,
                MissionName = i.Mission.Name,
                UavId = i.Mission.UAVId,
                UavCode = i.Mission.UAV != null ? i.Mission.UAV.Code : null,
                UavName = i.Mission.UAV != null ? i.Mission.UAV.Name : null,
                Title = i.Title,
                Description = i.Description,
                Type = i.Type,
                Severity = i.Severity ?? "Medium",
                Status = i.Status,
                Latitude = i.Latitude,
                Longitude = i.Longitude,
                CreatedAt = i.CreatedAt,
                AcknowledgedAt = i.AcknowledgedAt,
                ResolvedAt = i.ResolvedAt
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<IncidentDetailResponse> AcknowledgeAsync(
        Guid incidentId,
        Guid currentUserId,
        bool isManager,
        string? notes,
        CancellationToken cancellationToken = default)
    {
        var incident = await GetForMutationAsync(incidentId, currentUserId, isManager, cancellationToken);
        if (incident.Status == "Resolved")
        {
            throw new InvalidOperationException("A resolved incident cannot be acknowledged.");
        }
        if (incident.Status == "Acknowledged")
        {
            return await GetRequiredDetailAsync(incidentId, currentUserId, isManager, cancellationToken);
        }

        var now = DateTimeOffset.UtcNow;
        incident.Status = "Acknowledged";
        incident.AcknowledgedAt = now;
        incident.AcknowledgedBy = currentUserId;
        await _lifecycle.AppendTransitionAndAlertsAsync(
            incident, "Open", "Acknowledged", "Acknowledged", notes,
            currentUserId, now, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
        return await GetRequiredDetailAsync(incidentId, currentUserId, isManager, cancellationToken);
    }

    public async Task<IncidentDetailResponse> ResolveAsync(
        Guid incidentId,
        Guid currentUserId,
        bool isManager,
        string resolutionNotes,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(resolutionNotes))
        {
            throw new ArgumentException("Resolution notes are required.");
        }

        var incident = await GetForMutationAsync(incidentId, currentUserId, isManager, cancellationToken);
        if (incident.Status == "Resolved")
        {
            return await GetRequiredDetailAsync(incidentId, currentUserId, isManager, cancellationToken);
        }

        var previous = incident.Status;
        var now = DateTimeOffset.UtcNow;
        incident.Status = "Resolved";
        incident.ResolvedAt = now;
        incident.ResolvedBy = currentUserId;
        incident.ResolutionNotes = resolutionNotes.Trim();
        await _lifecycle.AppendTransitionAndAlertsAsync(
            incident, previous, "Resolved", "Resolved", incident.ResolutionNotes,
            currentUserId, now, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
        return await GetRequiredDetailAsync(incidentId, currentUserId, isManager, cancellationToken);
    }

    public async Task<List<IncidentTransitionResponse>> GetHistoryAsync(
        Guid incidentId,
        Guid? currentUserId,
        bool isManager,
        CancellationToken cancellationToken = default)
    {
        _ = await GetRequiredDetailAsync(incidentId, currentUserId, isManager, cancellationToken);
        return await _context.IncidentTransitions
            .AsNoTracking()
            .Where(x => x.IncidentId == incidentId)
            .OrderBy(x => x.CreatedAt)
            .Select(x => new IncidentTransitionResponse
            {
                Id = x.Id,
                FromStatus = x.FromStatus,
                ToStatus = x.ToStatus,
                EventType = x.EventType,
                Notes = x.Notes,
                ChangedBy = x.ChangedBy,
                ChangedByName = x.Actor == null ? null : x.Actor.FullName,
                CreatedAt = x.CreatedAt
            })
            .ToListAsync(cancellationToken);
    }

    public Task<List<IncidentAlertResponse>> GetUnreadAlertsAsync(
        Guid currentUserId,
        CancellationToken cancellationToken = default) =>
        _context.IncidentAlertDeliveries
            .AsNoTracking()
            .Where(x => x.RecipientUserId == currentUserId && x.ReadAt == null)
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new IncidentAlertResponse
            {
                Id = x.Id,
                IncidentId = x.IncidentId,
                MissionId = x.Incident.MissionId,
                IncidentTitle = x.Incident.Title,
                Severity = x.Incident.Severity ?? "Medium",
                EventType = x.EventType,
                CreatedAt = x.CreatedAt,
                ReadAt = x.ReadAt
            })
            .ToListAsync(cancellationToken);

    public async Task MarkAlertReadAsync(
        Guid alertId,
        Guid currentUserId,
        CancellationToken cancellationToken = default)
    {
        var alert = await _context.IncidentAlertDeliveries
            .FirstOrDefaultAsync(x => x.Id == alertId && x.RecipientUserId == currentUserId, cancellationToken)
            ?? throw new KeyNotFoundException("Alert delivery was not found.");
        alert.ReadAt ??= DateTimeOffset.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);
    }

    private async Task<Incident> GetForMutationAsync(
        Guid incidentId, Guid currentUserId, bool isManager, CancellationToken cancellationToken)
    {
        var incident = await _context.Incidents
            .Include(x => x.Mission)
            .FirstOrDefaultAsync(x => x.Id == incidentId, cancellationToken)
            ?? throw new KeyNotFoundException("Incident was not found.");
        if (!isManager && incident.Mission.CreatedBy != currentUserId)
        {
            throw new UnauthorizedAccessException("You do not have permission to update this incident.");
        }
        return incident;
    }

    private async Task<IncidentDetailResponse> GetRequiredDetailAsync(
        Guid incidentId, Guid? currentUserId, bool isManager, CancellationToken cancellationToken) =>
        await GetIncidentByIdAsync(incidentId, currentUserId, isManager, cancellationToken)
        ?? throw new KeyNotFoundException("Incident was not found.");

    private static EvidenceParseResult ParseEvidence(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return new EvidenceParseResult();
        }

        try
        {
            using var document = JsonDocument.Parse(raw);
            var root = document.RootElement;
            return new EvidenceParseResult
            {
                Payload = JsonSerializer.Deserialize<object>(raw),
                SourceTimestamp = ReadDate(root, "sourceTimestamp"),
                ReceivedAt = ReadDate(root, "receivedAt"),
                AppliedRule = root.TryGetProperty("appliedRule", out var rule) ? rule.GetString() : null,
                Threshold = root.TryGetProperty("threshold", out var threshold) && threshold.ValueKind == JsonValueKind.Number
                    ? threshold.GetDouble()
                    : null
            };
        }
        catch (JsonException)
        {
            return new EvidenceParseResult { Payload = raw };
        }
    }

    private static DateTimeOffset? ReadDate(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return DateTimeOffset.TryParse(value.GetString(), out var parsed) ? parsed : null;
    }

    private sealed class EvidenceParseResult
    {
        public object? Payload { get; set; }

        public DateTimeOffset? SourceTimestamp { get; set; }

        public DateTimeOffset? ReceivedAt { get; set; }

        public string? AppliedRule { get; set; }

        public double? Threshold { get; set; }
    }
}
