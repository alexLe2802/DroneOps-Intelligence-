using System.Data;
using DroneOps.Application.Interfaces.Notifications;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using DroneOps.Application.DTOs.Request.UAVs;
using DroneOps.Domain.Entities;
using DroneOps.Persistence.Data;
using Microsoft.EntityFrameworkCore;
namespace DroneOps.Application.Services.UAVs;

public sealed class UavReviewDecision
{
    public long? ExpectedVersion { get; set; }
    public Guid? RequestId { get; set; }
    public string Reason { get; set; } = "";
}

public sealed class UavReviewWorkflow(AppDbContext db, INotificationService? notifications = null, ILogger<UavReviewWorkflow>? logger = null)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    // Only Operator-editable registry fields; never copy ownership, status, FC binding or media.
    private static readonly System.Reflection.PropertyInfo[] Fields = typeof(UpdateUavRequest).GetProperties()
        .Where(p => p.Name != nameof(UpdateUavRequest.ExpectedVersion)).ToArray();
    public static UpdateUavRequest Snapshot(UAV uav)
    {
        var value = new UpdateUavRequest();
        foreach (var field in Fields) field.SetValue(value, typeof(UAV).GetProperty(field.Name)!.GetValue(uav));
        return value;
    }
    private static string Encode(UpdateUavRequest value)
    {
        var snapshot = new UpdateUavRequest();
        foreach (var field in Fields) field.SetValue(snapshot, field.GetValue(value));
        return JsonSerializer.Serialize(snapshot, Json);
    }
    public void RecordRegistration(UAV uav, Guid actor) => db.UavReviewRequests.Add(new UavReviewRequest
    {
        Id = Guid.NewGuid(), UavId = uav.Id, SubmittedBy = actor, SubmittedAt = DateTimeOffset.UtcNow,
        Kind = "Registration", BaseVersion = uav.RegistryVersion,
        ProposedInformationJson = Encode(Snapshot(uav))
    });
    public async Task<object> ReadAsync(Guid id, Guid actor, bool manager, CancellationToken ct)
    {
        var uav = await db.UAVs.AsNoTracking().Include(x => x.Pilot).SingleOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new KeyNotFoundException("UAV not found.");
        if (!manager && uav.Pilot.UserId != actor) throw new UnauthorizedAccessException("UAV is outside your scope.");
        var history = await db.UavReviewRequests.AsNoTracking().Where(x => x.UavId == id)
            .OrderByDescending(x => x.SubmittedAt).ThenByDescending(x => x.Id).ToListAsync(ct);
        object View(UavReviewRequest row) => new { row.Id, row.Kind, row.Status, row.SubmittedBy, row.SubmittedAt,
            row.BaseVersion, row.ReviewedBy, row.ReviewedAt, row.Reason,
            CurrentAtSubmission = JsonSerializer.Deserialize<JsonElement>(row.BaseInformationJson),
            Proposed = JsonSerializer.Deserialize<JsonElement>(row.ProposedInformationJson) };
        return new { uav.RegistryVersion, uav.Status, HasApprovedInformation = IsApproved(uav),
            Current = Snapshot(uav), Pending = history.Where(x => x.Status == "Pending").Select(View).SingleOrDefault(),
            History = history.Select(View), LegacyHistoryUnavailable = history.Count == 0 };
    }
    public async Task StageAsync(Guid id, Guid actor, UpdateUavRequest proposed, CancellationToken ct)
    {
        await LockedAsync(id, async uav =>
        {
            var owner = await db.Pilots.SingleAsync(x => x.Id == uav.PilotId, ct);
            if (owner.UserId != actor) throw new UnauthorizedAccessException("UAV is outside your scope.");
            if (proposed.ExpectedVersion != uav.RegistryVersion) throw new InvalidOperationException("UAV changed. Reload before submitting.");
            var before = Encode(Snapshot(uav));
            var after = Encode(proposed);
            if (before == after && uav.Status != "Rejected") return;
            await EnsureNotFlyingAsync(id, ct);
            if (await db.DroneHardwareIdentities.AnyAsync(x => x.UavId == id, ct) &&
                (uav.Code != proposed.Code || uav.SerialNumber != proposed.SerialNumber || uav.Manufacturer != proposed.Manufacturer || uav.Model != proposed.Model))
                throw new InvalidOperationException("Bound aircraft identity cannot be replaced through a registry update. Use verified FC replacement.");
            if (await db.UAVs.AnyAsync(x => x.Id != id && x.Code.ToLower() == proposed.Code.ToLower(), ct))
                throw new InvalidOperationException("UAV code already exists.");
            var pending = await db.UavReviewRequests.SingleOrDefaultAsync(x => x.UavId == id && x.Status == "Pending", ct);
            if (pending is not null) throw new InvalidOperationException("A proposal is awaiting review. Wait for its decision before resubmitting.");
            db.UavReviewRequests.Add(new UavReviewRequest
            {
                Id = Guid.NewGuid(), UavId = id, SubmittedBy = actor, SubmittedAt = DateTimeOffset.UtcNow,
                BaseVersion = uav.RegistryVersion, BaseInformationJson = before, ProposedInformationJson = after,
                Kind = IsApproved(uav) ? "Update" : "Registration"
            });
            // Pending update is independent from the operational status and approved fields.
            if (!IsApproved(uav)) uav.Status = "Pending";
            uav.HasPendingProposal = true;
            uav.RegistryVersion++;
        }, ct);
    }
    public async Task DecideAsync(Guid id, Guid actor, string action, UavReviewDecision decision, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(decision.Reason) || decision.Reason.Trim().Length > 500)
            throw new ArgumentException("A decision reason of 1–500 characters is required.");
        await LockedAsync(id, async uav =>
        {
            if (decision.ExpectedVersion != uav.RegistryVersion) throw new InvalidOperationException("UAV changed. Reload before deciding.");
            await EnsureNotFlyingAsync(id, ct);
            if (action is "approve" or "reject")
            {
                var pending = await db.UavReviewRequests.SingleOrDefaultAsync(x => x.UavId == id && x.Status == "Pending", ct);
                // Legacy pending records have no reconstructable historic actor/time; reviewed current snapshot is explicit.
                if (pending is null && uav.Status is "Pending" or "Pending Review")
                {
                    if (decision.RequestId is not null) throw new InvalidOperationException("Review request changed. Reload.");
                    pending = new UavReviewRequest { Id = Guid.NewGuid(), UavId = id, Kind = "LegacyRegistration",
                        SubmittedBy = null, SubmittedAt = null,
                        BaseVersion = uav.RegistryVersion, ProposedInformationJson = Encode(Snapshot(uav)) };
                    db.UavReviewRequests.Add(pending);
                }
                else if (pending is null || pending.Id != decision.RequestId) throw new InvalidOperationException("Review request changed. Reload.");
                if (action == "approve")
                {
                    if (pending.SubmittedBy == actor) throw new InvalidOperationException("A Manager cannot approve their own UAV submission.");
                    if (uav.ImageStoragePath is not null && uav.ImageModerationStatus != "Approved")
                        throw new InvalidOperationException("Image moderation must pass before UAV approval. It is not Manager approval.");
                    var value = JsonSerializer.Deserialize<UpdateUavRequest>(pending.ProposedInformationJson, Json)!;
                    if (await db.UAVs.AnyAsync(x => x.Id != id && x.Code.ToLower() == value.Code.ToLower(), ct))
                        throw new InvalidOperationException("Proposed UAV code is already in use.");
                    foreach (var field in Fields) typeof(UAV).GetProperty(field.Name)!.SetValue(uav, field.GetValue(value));
                    if (!IsApproved(uav)) uav.Status = "Active";
                    uav.HasApprovedInformation = true;
                    var missionIds = await db.Missions.Where(x => x.UAVId == id && x.Status == "Approved").Select(x => x.Id).ToListAsync(ct);
                    var prepared = await db.MissionAutomationStates.Where(x => missionIds.Contains(x.MissionId)).ToListAsync(ct);
                    foreach (var preparation in prepared)
                    { preparation.ConfirmedAt = null; preparation.ConfirmedPlanHash = null; preparation.ConnectionId = null; }

                }
                else if (!IsApproved(uav)) uav.Status = "Rejected";
                uav.HasPendingProposal = false;
                pending.Status = action == "approve" ? "Approved" : "Rejected";
                pending.ReviewedBy = actor; pending.ReviewedAt = DateTimeOffset.UtcNow; pending.Reason = decision.Reason.Trim();
            }
            else
            {
                if (decision.RequestId is not null) throw new ArgumentException("Status changes must not include a proposal identity.");
                if (action == "activate" && (!IsApproved(uav) || uav.Status != "Inactive"))
                    throw new InvalidOperationException("Only previously approved Inactive UAVs can be reactivated.");
                if (action == "deactivate" && uav.Status != "Active") throw new InvalidOperationException("Only Active UAVs can be deactivated.");
                if (action is not ("activate" or "deactivate")) throw new ArgumentException("Unknown UAV action.");
                var before = Encode(Snapshot(uav));
                var previousStatus = uav.Status;
                uav.Status = action == "activate" ? "Active" : "Inactive";
                db.UavReviewRequests.Add(new UavReviewRequest { Id = Guid.NewGuid(), UavId = id,
                    Kind = action, Status = uav.Status, SubmittedBy = actor, SubmittedAt = DateTimeOffset.UtcNow,
                    BaseVersion = uav.RegistryVersion, BaseInformationJson = before, ProposedInformationJson = before,
                    ReviewedBy = actor, ReviewedAt = DateTimeOffset.UtcNow, Reason = $"{previousStatus} → {uav.Status}: {decision.Reason.Trim()}" });
            }
            uav.RegistryVersion++;
        }, ct);
        if (notifications is not null)
        {
            try
            {
                var owner = await db.UAVs.AsNoTracking().Where(x => x.Id == id).Select(x => x.Pilot.UserId).SingleAsync(ct);
                await notifications.PublishToUserAsync(owner, "UavReviewDecision", "UAV review decision",
                    $"UAV action '{action}' saved. Reason: {decision.Reason.Trim()}", actor, id, "UAV", $"/portal/uav/{id}", ct);
                await notifications.RefreshUavManualReviewAsync(ct);
            }
            catch (Exception exception) when (!ct.IsCancellationRequested)
            { logger?.LogWarning(exception, "UAV decision saved but notification failed for {UavId}", id); }
        }
    }
    public static bool IsApproved(UAV uav) => uav.HasApprovedInformation || uav.Status is "Active" or "Inactive";
    private async Task EnsureNotFlyingAsync(Guid id, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var cutoff = now.AddSeconds(-10);
        if (await db.TelemetryRecords.AnyAsync(x => x.UAVId == id && x.IsValid && x.Armed == true &&
            x.ReceivedAt >= cutoff && x.ReceivedAt <= now.AddSeconds(5) && x.SourceTimestamp >= cutoff && x.SourceTimestamp <= now.AddSeconds(5), ct))
            throw new InvalidOperationException("Fresh FC telemetry reports this UAV armed. Registry changes and deactivation are blocked.");
        if (await db.MissionFlightExecutions.AnyAsync(x => x.UavId == id && x.CompletedAt == null, ct) ||
            await db.Missions.AnyAsync(x => x.UAVId == id && (x.Status == "InProgress" || x.Status == "In Progress" || x.Status == "Active" || x.Status == "Flying" || x.Status == "Executing"), ct))
            throw new InvalidOperationException("UAV has an active flight/execution. Registry changes and deactivation are blocked.");
    }
    private async Task LockedAsync(Guid id, Func<UAV, Task> operation, CancellationToken ct)
    {
        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var transaction = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;
            var uav = db.Database.IsRelational()
                ? await db.UAVs.FromSqlInterpolated($"SELECT * FROM \"UAV\" WHERE \"Id\"={id} FOR UPDATE").SingleOrDefaultAsync(ct)
                : await db.UAVs.SingleOrDefaultAsync(x => x.Id == id, ct);
            if (uav is null) throw new KeyNotFoundException("UAV not found.");
            if (db.Database.IsRelational()) await db.Entry(uav).ReloadAsync(ct);
            try { await operation(uav); await db.SaveChangesAsync(ct); if (transaction is not null) await transaction.CommitAsync(ct); }
            catch (DbUpdateConcurrencyException) { throw new InvalidOperationException("UAV changed concurrently. Reload before retrying."); }
            catch (DbUpdateException) { throw new InvalidOperationException("UAV update conflicted with another request. Reload before retrying."); }
        });
    }
}
