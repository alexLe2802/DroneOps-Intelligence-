using System.Data;
using DroneOps.Application.DTOs.Request.License;
using DroneOps.Application.DTOs.Response.License;
using DroneOps.Application.Interfaces.Notifications;
using DroneOps.Domain.Entities;
using DroneOps.Persistence.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DroneOps.Application.Services.License;

public interface IPilotLicenseWorkflow
{
    Task<PilotLicenseStatusResponse> DecideAsync(
        Guid id,
        Guid actorId,
        string action,
        PilotLicenseDecisionRequest decision,
        CancellationToken ct = default);

    Task<IReadOnlyList<PilotLicenseAuditResponse>> GetHistoryAsync(
        Guid id,
        CancellationToken ct = default);

    Task<PilotLicenseStatusResponse?> GetStatusAsync(
        Guid id,
        CancellationToken ct = default);
}

public sealed class PilotLicenseWorkflow(
    AppDbContext db,
    INotificationService? notifications = null,
    ILogger<PilotLicenseWorkflow>? logger = null) : IPilotLicenseWorkflow
{
    public async Task<PilotLicenseStatusResponse?> GetStatusAsync(
        Guid id,
        CancellationToken ct = default)
    {
        var pilot = await db.Pilots.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == id || x.UserId == id, ct);

        if (pilot is null)
        {
            return null;
        }

        return new PilotLicenseStatusResponse
        {
            PilotId = pilot.Id,
            UserId = pilot.UserId,
            PilotLicenseNo = pilot.PilotLicenseNo,
            LicenseStatus = pilot.LicenseStatus,
            LicenseVersion = pilot.LicenseVersion,
            UpdatedAt = DateTimeOffset.UtcNow
        };
    }

    public async Task<IReadOnlyList<PilotLicenseAuditResponse>> GetHistoryAsync(
        Guid id,
        CancellationToken ct = default)
    {
        var pilot = await db.Pilots.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == id || x.UserId == id, ct)
            ?? throw new KeyNotFoundException("Pilot not found.");

        var history = await db.PilotLicenseAudits.AsNoTracking()
            .Where(x => x.PilotId == pilot.Id)
            .OrderByDescending(x => x.OccurredAt)
            .Select(x => new PilotLicenseAuditResponse
            {
                Id = x.Id,
                PilotId = x.PilotId,
                Action = x.Action,
                PreviousStatus = x.PreviousStatus,
                NewStatus = x.NewStatus,
                Reason = x.Reason,
                ActorId = x.ActorId,
                OccurredAt = x.OccurredAt
            })
            .ToListAsync(ct);

        return history;
    }

    public async Task<PilotLicenseStatusResponse> DecideAsync(
        Guid id,
        Guid actorId,
        string action,
        PilotLicenseDecisionRequest decision,
        CancellationToken ct = default)
    {
        action = action.ToLowerInvariant().Trim();
        if (action is not ("activate" or "deactivate"))
        {
            throw new ArgumentException("Action must be either 'activate' or 'deactivate'.");
        }

        if (string.IsNullOrWhiteSpace(decision.Reason) || decision.Reason.Trim().Length > 500)
        {
            throw new ArgumentException("A decision reason of 1–500 characters is required.");
        }

        PilotLicenseStatusResponse? response = null;

        await LockedPilotAsync(id, async pilot =>
        {
            if (decision.ExpectedVersion.HasValue && decision.ExpectedVersion.Value != pilot.LicenseVersion)
            {
                throw new InvalidOperationException("Pilot license record changed concurrently. Reload before deciding.");
            }

            if (action == "deactivate")
            {
                if (pilot.LicenseStatus != PilotLicenseStatus.Active)
                {
                    throw new InvalidOperationException("Only Active flight licenses can be deactivated.");
                }

                await EnsurePilotNotFlyingAsync(pilot.UserId, ct);
            }
            else if (action == "activate")
            {
                if (pilot.LicenseStatus == PilotLicenseStatus.Active)
                {
                    throw new InvalidOperationException("Flight license is already Active.");
                }

                if (pilot.LicenseExpiredDate.HasValue &&
                    pilot.LicenseExpiredDate.Value < DateOnly.FromDateTime(DateTime.UtcNow))
                {
                    throw new InvalidOperationException("Cannot activate an expired license. An updated license must be submitted.");
                }
            }

            var previousStatus = pilot.LicenseStatus;
            pilot.LicenseStatus = action == "activate" ? PilotLicenseStatus.Active : PilotLicenseStatus.Inactive;
            pilot.LicenseVersion++;

            db.PilotLicenseAudits.Add(new PilotLicenseAudit
            {
                Id = Guid.NewGuid(),
                PilotId = pilot.Id,
                Action = action,
                PreviousStatus = previousStatus,
                NewStatus = pilot.LicenseStatus,
                Reason = decision.Reason.Trim(),
                ActorId = actorId,
                OccurredAt = DateTimeOffset.UtcNow
            });

            response = new PilotLicenseStatusResponse
            {
                PilotId = pilot.Id,
                UserId = pilot.UserId,
                PilotLicenseNo = pilot.PilotLicenseNo,
                LicenseStatus = pilot.LicenseStatus,
                LicenseVersion = pilot.LicenseVersion,
                UpdatedAt = DateTimeOffset.UtcNow,
                Reason = decision.Reason.Trim()
            };
        }, ct);

        if (notifications is not null && response is not null)
        {
            try
            {
                var actionVerb = action == "activate" ? "activated" : "deactivated";
                await notifications.PublishToUserAsync(
                    response.UserId,
                    "PilotLicenseStatusChanged",
                    "Flight License Status Updated",
                    $"Your flight license ({response.PilotLicenseNo}) has been {actionVerb}. Reason: {decision.Reason.Trim()}",
                    actorId,
                    response.PilotId,
                    "PilotLicense",
                    "/portal/profile",
                    ct);
            }
            catch (Exception exception) when (!ct.IsCancellationRequested)
            {
                logger?.LogWarning(exception, "License status changed but notification failed for Pilot {PilotId}", response.PilotId);
            }
        }

        return response!;
    }

    private async Task EnsurePilotNotFlyingAsync(Guid userId, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var cutoff = now.AddSeconds(-10);

        // 1. Kiểm tra telemetry vũ trang (armed) từ bất kỳ UAV nào do phi công này sở hữu
        var isArmed = await db.TelemetryRecords.AnyAsync(x =>
            db.UAVs.Any(u => u.Id == x.UAVId && u.Pilot.UserId == userId) &&
            x.IsValid && x.Armed == true &&
            x.ReceivedAt >= cutoff && x.ReceivedAt <= now.AddSeconds(5) &&
            x.SourceTimestamp >= cutoff && x.SourceTimestamp <= now.AddSeconds(5), ct);

        if (isArmed)
        {
            throw new InvalidOperationException("Fresh FC telemetry reports an aircraft allocated to this operator is currently armed. License deactivation is blocked.");
        }

        // 2. Kiểm tra chuyến bay đang thực thi (MissionFlightExecution chưa Complete)
        var userUavIds = db.UAVs.Where(u => u.Pilot.UserId == userId).Select(u => u.Id);
        var userMissionIds = db.Missions.Where(m => m.CreatedBy == userId).Select(m => m.Id);

        var hasActiveExecution = await db.MissionFlightExecutions
            .AnyAsync(x => x.CompletedAt == null && (userUavIds.Contains(x.UavId) || userMissionIds.Contains(x.MissionId)), ct);

        if (hasActiveExecution)
        {
            throw new InvalidOperationException("Operator is currently conducting an active flight execution. License deactivation is blocked.");
        }

        // 3. Kiểm tra nhiệm vụ có trạng thái InProgress / Flying / Executing
        var activeStatuses = new[] { "InProgress", "In Progress", "Active", "Flying", "Executing" };
        var hasActiveMission = await db.Missions
            .AnyAsync(x => x.CreatedBy == userId && activeStatuses.Contains(x.Status), ct);

        if (hasActiveMission)
        {
            throw new InvalidOperationException("Operator has a flight mission currently in progress. License deactivation is blocked.");
        }
    }

    private async Task LockedPilotAsync(Guid id, Func<Pilot, Task> operation, CancellationToken ct)
    {
        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var transaction = db.Database.IsRelational()
                ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct)
                : null;

            var pilot = db.Database.IsRelational()
                ? await db.Pilots.FromSqlInterpolated($"SELECT * FROM \"Pilot\" WHERE \"Id\"={id} OR \"UserId\"={id} FOR UPDATE").SingleOrDefaultAsync(ct)
                : await db.Pilots.SingleOrDefaultAsync(x => x.Id == id || x.UserId == id, ct);

            if (pilot is null)
            {
                throw new KeyNotFoundException("Pilot not found.");
            }

            if (db.Database.IsRelational())
            {
                await db.Entry(pilot).ReloadAsync(ct);
            }

            try
            {
                await operation(pilot);
                await db.SaveChangesAsync(ct);
                if (transaction is not null)
                {
                    await transaction.CommitAsync(ct);
                }
            }
            catch (DbUpdateConcurrencyException)
            {
                throw new InvalidOperationException("Pilot record changed concurrently. Reload before retrying.");
            }
            catch (DbUpdateException)
            {
                throw new InvalidOperationException("Pilot update conflicted with another request. Reload before retrying.");
            }
        });
    }
}
