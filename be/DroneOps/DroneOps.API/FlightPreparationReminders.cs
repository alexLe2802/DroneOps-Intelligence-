using DroneOps.Application.Interfaces.Notifications;
using DroneOps.Persistence.Data;
using Microsoft.EntityFrameworkCore;

namespace DroneOps.API;

// Uses the existing notification store as durable reminder history. Read/unread
// status never resets delivery limits. Locking the mission serializes API/worker instances.
public sealed class FlightPreparationReminders(AppDbContext db, MissionAutomationService automation,
    INotificationService notifications, ILogger<FlightPreparationReminders>? logger = null)
{
    public const string ConnectType = "FlightConnectionRequired";
    public const string ConfirmType = "FlightConfirmationRequired";

    public async Task ProcessDueAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var ids = await db.Missions.AsNoTracking().Where(m => m.AutomaticFlightRequested && m.Status == "Approved"
            && m.StartTime <= now.AddMinutes(5) && m.EndTime > now).OrderBy(m => m.StartTime).Select(m => m.Id).ToListAsync(ct);
        foreach (var id in ids)
        {
            try { await ProcessMissionAsync(id, ct); }
            catch (Exception error) when (!ct.IsCancellationRequested)
            { logger?.LogWarning(error, "Preparation reminder failed for {MissionId}; next cycle will retry", id); }
        }
    }

    public async Task ProcessConnectionAsync(Guid connectionId, CancellationToken ct)
    {
        var mission = await db.DroneConnections.AsNoTracking().Where(c => c.Id == connectionId && c.RevokedAt == null)
            .Select(c => (Guid?)c.MissionId).SingleOrDefaultAsync(ct);
        if (mission.HasValue) await ProcessMissionAsync(mission.Value, ct);
    }

    public async Task ProcessMissionAsync(Guid id, CancellationToken ct)
    {
        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM public.\"Mission\" WHERE \"Id\" = {id} FOR UPDATE", ct);
            var now = DateTimeOffset.UtcNow;
            var mission = await db.Missions.AsNoTracking().SingleOrDefaultAsync(m => m.Id == id, ct);
            if (mission is null || !mission.AutomaticFlightRequested || mission.Status != "Approved"
                || mission.StartTime is null || mission.EndTime is null
                || !MissionAutomationService.InConfirmationWindow(now, new(mission.StartTime.Value, TimeSpan.Zero), new(mission.EndTime.Value, TimeSpan.Zero))
                || !await db.Users.AnyAsync(u => u.Id == mission.CreatedBy && u.IsActive, ct)) return;

            var state = await automation.ReadAsync(id, mission.CreatedBy, ct);
            string type, title, message;
            if (!state.GatewayConnected)
            {
                if (await db.Notifications.AnyAsync(n => n.RecipientUserId == mission.CreatedBy && n.ReferenceId == id && n.Type == ConnectType, ct)) return;
                type = ConnectType;
                title = "Connect your drone for the upcoming mission";
                message = $"Mission '{mission.Name}' is awaiting its drone. Open Live Monitoring and connect the assigned aircraft before the mission ends.";
            }
            else
            {
                if (!state.DroneConnected || !state.PreflightReady || state.ConfirmationRecorded) return;
                var cutoff = now.AddMinutes(-2);
                if (await db.Notifications.AnyAsync(n => n.RecipientUserId == mission.CreatedBy && n.ReferenceId == id
                    && n.Type == ConfirmType && n.CreatedAt > cutoff, ct)) return;
                type = ConfirmType;
                title = "Confirm automatic flight preparation";
                message = $"Drone data and preflight checks for '{mission.Name}' are ready. Review and confirm in Live Monitoring before the mission ends.";
            }
            // Recheck the clock immediately before publishing, including slow preflight/database reads.
            if (DateTimeOffset.UtcNow >= state.EndsAt) return;
            message += $" Schedule: {state.ScheduledAt.ToOffset(TimeSpan.FromHours(7)):HH:mm dd/MM/yyyy}–{state.EndsAt.ToOffset(TimeSpan.FromHours(7)):HH:mm dd/MM/yyyy} (GMT+7).";
            await notifications.PublishToUserAsync(mission.CreatedBy, type, title, message, null, id,
                "Mission", $"/portal/live-monitoring?missionId={id}", ct);
            await transaction.CommitAsync(ct);
        });
    }
}
