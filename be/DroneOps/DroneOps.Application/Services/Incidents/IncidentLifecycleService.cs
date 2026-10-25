using DroneOps.Application.Interfaces.Incidents;
using DroneOps.Domain.Entities;
using DroneOps.Persistence.Data;
using Microsoft.EntityFrameworkCore;

namespace DroneOps.Application.Services.Incidents;

/// <summary>
/// Tập trung việc đổi trạng thái incident, ghi lịch sử append-only và tạo alert bền vững.
/// Mọi detector phải đi qua service này để không bỏ sót audit/notification.
/// </summary>
public sealed class IncidentLifecycleService : IIncidentLifecycleService
{
    private static readonly string[] ManagerRoles = ["operation_manager", "operations_manager"];
    private readonly AppDbContext _context;

    public IncidentLifecycleService(AppDbContext context) => _context = context;

    public async Task<Guid?> RaiseIfOpenMissingAsync(
        Incident incident,
        CancellationToken cancellationToken = default)
    {
        var exists = await _context.Incidents.AnyAsync(
            x => x.MissionId == incident.MissionId && x.Type == incident.Type && x.Status != "Resolved",
            cancellationToken);
        if (exists)
        {
            return null;
        }

        incident.Status = "Open";
        incident.CreatedAt = incident.CreatedAt == default ? DateTimeOffset.UtcNow : incident.CreatedAt;
        await _context.Incidents.AddAsync(incident, cancellationToken);
        await AppendTransitionAndAlertsAsync(
            incident, string.Empty, "Open", "Raised", notes: incident.Description,
            changedBy: null, incident.CreatedAt, cancellationToken);
        return incident.Id;
    }

    public async Task<bool> ResolveOpenByRuleAsync(
        Guid missionId,
        string type,
        string notes,
        CancellationToken cancellationToken = default)
    {
        var incident = await _context.Incidents
            .Where(x => x.MissionId == missionId && x.Type == type && x.Status != "Resolved")
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (incident == null)
        {
            return false;
        }

        var previous = incident.Status;
        var now = DateTimeOffset.UtcNow;
        incident.Status = "Resolved";
        incident.ResolvedAt = now;
        incident.ResolvedBy = null;
        incident.ResolutionNotes = notes;
        await AppendTransitionAndAlertsAsync(
            incident, previous, "Resolved", "AutoResolved", notes,
            changedBy: null, now, cancellationToken);
        return true;
    }

    public async Task AppendTransitionAndAlertsAsync(
        Incident incident,
        string fromStatus,
        string toStatus,
        string eventType,
        string? notes,
        Guid? changedBy,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken)
    {
        var transition = new IncidentTransition
        {
            Id = Guid.NewGuid(),
            IncidentId = incident.Id,
            FromStatus = fromStatus,
            ToStatus = toStatus,
            EventType = eventType,
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
            ChangedBy = changedBy,
            CreatedAt = createdAt
        };
        await _context.IncidentTransitions.AddAsync(transition, cancellationToken);

        // Alert được phát cho pilot sở hữu mission và toàn bộ operations manager.
        var recipients = await _context.Missions
            .Where(x => x.Id == incident.MissionId)
            .Select(x => x.CreatedBy)
            .Concat(_context.Users
                .Where(x => ManagerRoles.Contains(x.Role.Name))
                .Select(x => x.Id))
            .Distinct()
            .ToListAsync(cancellationToken);

        foreach (var recipient in recipients)
        {
            await _context.IncidentAlertDeliveries.AddAsync(new IncidentAlertDelivery
            {
                Id = Guid.NewGuid(),
                IncidentId = incident.Id,
                TransitionId = transition.Id,
                RecipientUserId = recipient,
                EventType = eventType,
                CreatedAt = createdAt
            }, cancellationToken);
        }
    }
}
