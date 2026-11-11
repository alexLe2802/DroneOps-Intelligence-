using System.Text.Json;
using DroneOps.Application.DTOs.Request.Missions;
using DroneOps.Application.DTOs.Response.Missions;
using DroneOps.Application.Interfaces;
using DroneOps.Application.Interfaces.Validators;
using DroneOps.Application.Validators.Missions;
using DroneOps.Domain.Entities;
using DroneOps.Persistence.Interfaces;
using DroneOps.Persistence.Data;
using Microsoft.EntityFrameworkCore;

namespace DroneOps.Application.Services.Missions;

public sealed class MissionWorkflowService(
    IMissionWorkflowRepository missionRepository,
    IMissionValidationService validation,
    AppDbContext db) : IMissionWorkflowService
{
    public async Task<Mission> SavePlanAsync(Guid actor, CreateMissionRequest request, Guid? id,
        CancellationToken cancellationToken = default, bool manager = false)
    {
        var operationId = request.OperationId ?? Guid.NewGuid();
        if (operationId == Guid.Empty) throw new ArgumentException("Operation identity is required.");
        var structural = await new CreateMissionRequestValidator().ValidateAsync(request, cancellationToken);
        if (!structural.IsValid) throw new ArgumentException(string.Join(" ", structural.Errors.Select(x => x.ErrorMessage)));

        return await missionRepository.ExecuteSerializableAsync(async ct =>
        {
            if (!id.HasValue)
            {
                // The client reuses the identity after a lost response. Never create a second Draft.
                var existing = await missionRepository.GetByIdWithWaypointsAsync(operationId, ct);
                if (existing is not null)
                {
                    if (existing.CreatedBy != actor) throw new UnauthorizedAccessException("Mission is outside your scope.");
                    if (MissionValidationService.Hash(existing) != MissionValidationService.Hash(MissionPlans.Propose(existing, request)))
                        throw new InvalidOperationException("Operation identity was already used with another plan.");
                    return existing;
                }
                var draft = MissionPlans.Propose(new Mission
                {
                    Id = operationId, CreatedBy = actor, CreatedAt = DateTime.UtcNow, Status = "Draft"
                }, request);
                var aircraft = await db.UAVs.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.UavId, ct);
                var pilot = await db.Pilots.AsNoTracking().Include(x => x.User).SingleOrDefaultAsync(x => x.UserId == actor, ct);
                if (aircraft is null || pilot is null || aircraft.PilotId != pilot.Id || aircraft.Status != "Active" ||
                    pilot.User is null || !pilot.User.IsActive || string.IsNullOrWhiteSpace(pilot.PilotLicenseNo) ||
                    pilot.LicenseIssuedDate is null || pilot.LicenseExpiredDate is null ||
                    pilot.LicenseIssuedDate > DateOnly.FromDateTime(request.StartTime) ||
                    pilot.LicenseExpiredDate < DateOnly.FromDateTime(request.EndTime))
                    throw new ArgumentException("Draft creation requires an active assigned UAV and a pilot eligible throughout the scheduled flight.");
                await missionRepository.AddAsync(draft, ct);
                return draft;
            }

            var current = await AuthorizedMissionAsync(id.Value, actor, manager, ct);
            var proposal = MissionPlans.Propose(current, request);
            var replay = await db.MissionUpdateRequests.SingleOrDefaultAsync(x => x.Id == operationId, ct);
            if (replay is not null)
            {
                if (replay.MissionId != current.Id || replay.ActorId != actor ||
                    MissionValidationService.Hash(MissionPlans.Propose(current, Decode(replay.ProposedPlanJson))) != MissionValidationService.Hash(proposal))
                    throw new InvalidOperationException("Operation identity was already used with another proposal.");
                return current;
            }
            EnsureEditable(current);
            var pending = await db.MissionUpdateRequests.SingleOrDefaultAsync(x => x.MissionId == current.Id && x.Status == "Pending", ct);
            if (request.ExpectedPlanHash is not null &&
                (request.ExpectedPlanHash != MissionValidationService.Hash(current) || request.ExpectedPendingRequestId != pending?.Id))
                throw new InvalidOperationException("The plan or pending request changed. Reload before saving.");

            if (pending is not null) pending.Status = "Superseded";
            // Release the unique pending slot before inserting its replacement, in the same transaction.
            await db.SaveChangesAsync(ct);
            await AddReviewAsync(current, proposal, actor, operationId, false, ct);
            return current;
        }, cancellationToken);
    }

    public async Task SubmitAsync(Guid id, Guid actor, Guid operationId, CancellationToken cancellationToken = default)
    {
        if (operationId == Guid.Empty) throw new ArgumentException("Operation identity is required.");
        await missionRepository.ExecuteSerializableAsync(async ct =>
        {
            var mission = await AuthorizedMissionAsync(id, actor, false, ct);
            var replay = await db.MissionUpdateRequests.SingleOrDefaultAsync(x => x.Id == operationId, ct);
            if (replay is not null)
            {
                if (replay.MissionId != id || replay.ActorId != actor || !replay.IsInitial)
                    throw new InvalidOperationException("Operation identity belongs to another request.");
                return true;
            }
            if (mission.Status is not ("Draft" or "Rejected"))
                throw new InvalidOperationException("Only Draft or Rejected missions can be submitted.");
            if (await db.MissionUpdateRequests.AnyAsync(x => x.MissionId == id && x.Status == "Pending", ct))
                throw new InvalidOperationException("A pending proposal already exists. Review it before submitting.");
            await AddReviewAsync(mission, MissionPlans.Propose(mission, MissionPlans.Dto(mission)), actor, operationId, true, ct);
            mission.Status = "Pending Review";
            return true;
        }, cancellationToken);
    }

    private async Task AddReviewAsync(Mission current, Mission proposal, Guid actor, Guid requestId, bool initial, CancellationToken ct)
    {
        var result = await validation.EvaluateAsync(proposal, initial ? "Creation" : "Update", ct);
        result.UpdateRequestId = requestId;
        // A failed spatial/safety check is evidence for review, never approval permission.
        await validation.StoreAsync(proposal, actor, result, true, ct);
        db.MissionUpdateRequests.Add(new MissionUpdateRequest
        {
            Id = requestId, MissionId = current.Id, ActorId = actor, IsInitial = initial, Status = "Pending",
            BasePlanHash = MissionValidationService.Hash(current),
            BasePlanJson = JsonSerializer.Serialize(MissionPlans.Dto(current), MissionValidationService.JsonOptions),
            ProposedPlanJson = JsonSerializer.Serialize(MissionPlans.Dto(proposal), MissionValidationService.JsonOptions),
            ValidationRunId = result.Id, CreatedAt = DateTimeOffset.UtcNow
        });
    }

    public async Task CancelAsync(Guid id, Guid actor, bool manager, string reason, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("Cancellation reason is required.");
        await missionRepository.ExecuteSerializableAsync(async ct =>
        {
            var mission = await AuthorizedMissionAsync(id, actor, manager, ct);
            if (mission.Status == "Cancelled") return true;
            EnsureEditable(mission);
            var requests = await db.MissionUpdateRequests.Where(x => x.MissionId == id && x.Status == "Pending").ToListAsync(ct);
            foreach (var request in requests)
            {
                request.Status = "Cancelled"; request.ReviewedBy = actor;
                request.ReviewedAt = DateTimeOffset.UtcNow; request.Reason = reason.Trim();
            }
            // Retain cancellation evidence even for a Draft that never had a review request.
            if (requests.Count == 0)
                db.MissionUpdateRequests.Add(new MissionUpdateRequest
                {
                    Id = Guid.NewGuid(), MissionId = id, ActorId = actor, Status = "Cancelled",
                    BasePlanHash = MissionValidationService.Hash(mission),
                    BasePlanJson = JsonSerializer.Serialize(MissionPlans.Dto(mission), MissionValidationService.JsonOptions),
                    ProposedPlanJson = JsonSerializer.Serialize(MissionPlans.Dto(mission), MissionValidationService.JsonOptions),
                    CreatedAt = DateTimeOffset.UtcNow, ReviewedAt = DateTimeOffset.UtcNow,
                    ReviewedBy = actor, Reason = reason.Trim()
                });
            mission.Status = "Cancelled";
            return true;
        }, cancellationToken);
    }

    public Task<MissionValidationResult> StartAsync(Guid id, Guid actor, bool manager, CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("Flight start requires trusted aircraft execution evidence. Use the authorized flight preparation workflow.");

    public async Task ConfirmHighRiskAsync(Guid missionId, Guid pilotId, CancellationToken cancellationToken = default,
        Guid? updateRequestId = null, Guid? validationRunId = null)
    {
        await missionRepository.ExecuteSerializableAsync(async ct =>
        {
            var mission = await AuthorizedMissionAsync(missionId, pilotId, false, ct);
            EnsureEditable(mission);
            var request = await PendingAsync(missionId, updateRequestId, ct);
            if (!validationRunId.HasValue || request.ValidationRunId != validationRunId)
                throw new InvalidOperationException("Validation changed. Reload the risk evidence before confirming.");
            if (request.BasePlanHash != MissionValidationService.Hash(mission))
                throw new InvalidOperationException("The current plan changed. Save and revalidate the proposal.");
            var evidence = await validation.ReadRunAsync(missionId, validationRunId.Value, pilotId, false, ct);
            if (evidence is null || !evidence.IsValid || evidence.Error || evidence.UpdateRequestId != request.Id)
                throw new InvalidOperationException("Fresh passing proposal validation is required before risk confirmation.");
            if (evidence.Weather is { Available: true } weather && weather.ExpiresAt <= DateTimeOffset.UtcNow)
                throw new InvalidOperationException("Weather evidence expired. Revalidate before confirming.");
            request.PilotConfirmedBy = pilotId; request.PilotConfirmedAt = DateTimeOffset.UtcNow;
            request.PilotConfirmedValidationRunId = validationRunId;
            if (mission.Status == "Pending Confirm") mission.Status = "Pending Review"; // legacy initial submissions
            return true;
        }, cancellationToken);
    }

    public async Task CancelHighRiskAsync(Guid missionId, Guid pilotId, CancellationToken cancellationToken = default,
        Guid? updateRequestId = null)
    {
        await missionRepository.ExecuteSerializableAsync(async ct =>
        {
            var mission = await AuthorizedMissionAsync(missionId, pilotId, false, ct);
            EnsureEditable(mission);
            var request = await PendingAsync(missionId, updateRequestId, ct);
            request.Status = "Cancelled"; request.ReviewedBy = pilotId; request.ReviewedAt = DateTimeOffset.UtcNow;
            request.Reason = "Pilot declined the pre-flight risk. Revise and validate before resubmitting.";
            if (request.IsInitial) mission.Status = "Draft";
            return true;
        }, cancellationToken);
    }

    private async Task<MissionUpdateRequest> PendingAsync(Guid id, Guid? requestId, CancellationToken ct)
    {
        if (!requestId.HasValue) throw new ArgumentException("Review request identity is required.");
        return await db.MissionUpdateRequests.SingleOrDefaultAsync(x => x.MissionId == id && x.Id == requestId && x.Status == "Pending", ct)
            ?? throw new InvalidOperationException("The pending review request changed. Reload before acting.");
    }

    private async Task<Mission> AuthorizedMissionAsync(Guid id, Guid actor, bool manager, CancellationToken ct)
    {
        var mission = await missionRepository.GetByIdWithWaypointsAsync(id, ct) ?? throw new KeyNotFoundException("Mission not found.");
        if (!manager && mission.CreatedBy != actor) throw new UnauthorizedAccessException("Mission is outside your scope.");
        return mission;
    }
    private static void EnsureEditable(Mission mission)
    {
        if (!MissionReviewPolicy.CanEdit(mission.Status)) throw new InvalidOperationException("Only pre-flight missions can be changed.");
    }
    private static CreateMissionRequest Decode(string json) => JsonSerializer.Deserialize<CreateMissionRequest>(json, MissionValidationService.JsonOptions)
        ?? throw new InvalidOperationException("Proposed plan is missing.");
}
