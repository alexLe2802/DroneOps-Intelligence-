using System.Security.Claims;
using DroneOps.Application.DTOs.Request;
using DroneOps.Application.Services.Missions;
using DroneOps.Application.Validators.Missions;
using DroneOps.Domain.Entities;
using DroneOps.Persistence.Data;
using DroneOps.Persistence.Interfaces;
using DroneOps.Persistence.Interfaces.Notifications;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DroneOps.API.Controllers.Missions;

[ApiController]
[Authorize(Roles = "uav_operator,operation_manager,operations_manager")]
[Route("api")]
public sealed class RegistryDocumentsController(AppDbContext db, IMissionWorkflowRepository workflow,
    INotificationRepository recipients) : ControllerBase
{
    private Guid Actor => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub"), out var id) ? id : Guid.Empty;
    private bool Manager => User.IsInRole("operation_manager") || User.IsInRole("operations_manager");

    private IQueryable<RegistryDocument> VisibleDocuments()
    {
        var documents = db.RegistryDocuments.AsNoTracking();
        if (Manager) return documents;
        return documents.Where(d => db.Missions.Any(m => m.Id == d.MissionId && m.CreatedBy == Actor) ||
            db.UAVs.Any(u => u.Id == d.UavId && u.Pilot.UserId == Actor));
    }

    [HttpGet("missions/{id:guid}/permits")]
    public async Task<IActionResult> Permits(Guid id, CancellationToken ct)
    {
        if (!await db.Missions.AnyAsync(m => m.Id == id && (Manager || m.CreatedBy == Actor), ct)) return NotFound();
        var planReview = await db.MissionApprovals.AsNoTracking().Where(a => a.MissionId == id && a.Status == "Approved")
            .OrderByDescending(a => a.ApprovedAt).ThenByDescending(a => a.Id)
            .Select(a => new { a.PermitDecision, a.PermitDocumentId, a.Comment }).FirstOrDefaultAsync(ct);
        var documents = await db.RegistryDocuments.AsNoTracking().Where(d => d.MissionId == id)
            .OrderByDescending(x => x.UploadedAt).ThenByDescending(x => x.Id)
            .Select(x => new { x.Id, x.Name, x.UploadedAt, x.Decision, x.Reason }).ToListAsync(ct);
        return Ok(new { documents, planReview });
    }

    [HttpGet("documents/{id:guid}")]
    public async Task<IActionResult> Download(Guid id, CancellationToken ct)
    {
        var doc = await VisibleDocuments().Where(d => d.Id == id).Select(d => new { d.Content, d.Name }).SingleOrDefaultAsync(ct);
        return doc is null ? NotFound() : File(doc.Content, "application/octet-stream", doc.Name);
    }

    [Authorize(Roles = "uav_operator")]
    [HttpPost("missions/{id:guid}/permits")]
    [RequestSizeLimit(3 * 1024 * 1024)]
    public async Task<IActionResult> Supplement(Guid id, RegistryFile file, CancellationToken ct)
    {
        RegistryDocument document;
        try { document = file.ToDocument(Actor); }
        catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
        return await workflow.ExecuteSerializableAsync<IActionResult>(async token =>
        {
            var mission = await db.Missions.SingleOrDefaultAsync(x => x.Id == id && x.CreatedBy == Actor, token);
            if (mission is null) return NotFound();
            if (mission.Status != "Awaiting Permit" || await HasPendingPlan(id, token))
                return Conflict(new { message = "Refresh the mission before submitting a permit." });
            document.MissionId = id;
            db.RegistryDocuments.Add(document);
            mission.Status = "Permit Review";
            foreach (var manager in await recipients.GetActiveOperationManagerIdsAsync(token))
                db.Notifications.Add(PermitNotifications.For(mission, manager, Actor, "Flight permit ready for review", document.Name));
            return NoContent();
        }, ct);
    }

    private Task<bool> HasPendingPlan(Guid id, CancellationToken ct) =>
        db.MissionUpdateRequests.AnyAsync(r => r.MissionId == id && r.Status == "Pending", ct);

    public sealed record PermitReview(Guid DocumentId, bool Accept, string? Reason);

    [Authorize(Roles = "operation_manager,operations_manager")]
    [HttpPost("missions/{id:guid}/permits/review")]
    public async Task<IActionResult> Review(Guid id, PermitReview review, CancellationToken ct)
    {
        var reason = review.Reason?.Trim();
        if ((!review.Accept && string.IsNullOrEmpty(reason)) || reason?.Length > 1000)
            return BadRequest(new { message = "Enter a correction reason of up to 1000 characters." });
        return await workflow.ExecuteSerializableAsync<IActionResult>(async token =>
        {
            var mission = await db.Missions.SingleOrDefaultAsync(x => x.Id == id, token);
            if (mission is null) return NotFound();
            if (mission.CreatedBy == Actor) return Forbid();
            var document = await db.RegistryDocuments.Where(x => x.MissionId == id)
                .OrderByDescending(x => x.UploadedAt).ThenByDescending(x => x.Id).FirstOrDefaultAsync(token);
            if (mission.Status != "Permit Review" || document is null || document.Id != review.DocumentId ||
                document.Decision != null || await HasPendingPlan(id, token))
                return Conflict(new { message = "The plan or permit has changed. Reload the mission." });
            if (review.Accept)
            {
                await FlightEligibility.LockAircraftAsync(db, mission.UAVId, token);
                var block = await FlightEligibility.BlockingReasonAsync(db, mission, token);
                if (block != null) return Conflict(new { message = block });
            }
            document.Decision = review.Accept ? "Accepted" : "Rejected";
            document.Reason = reason;
            document.ReviewedAt = DateTimeOffset.UtcNow;
            document.ReviewedBy = Actor;
            mission.Status = review.Accept ? "Approved" : "Awaiting Permit";
            db.Notifications.Add(PermitNotifications.For(mission, mission.CreatedBy, Actor,
                review.Accept ? "Flight permit accepted" : "Flight permit correction required",
                reason ?? "The mission may proceed to preflight."));
            return NoContent();
        }, ct);
    }
}
