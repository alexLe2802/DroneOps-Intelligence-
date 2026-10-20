using DroneOps.Application.DTOs.Request.Missions;
using DroneOps.Application.DTOs.Response.Missions;
using DroneOps.Domain.Entities;
using DroneOps.Persistence.Data;
using Microsoft.EntityFrameworkCore;
namespace DroneOps.Application.Services.Missions;

public sealed class MissionWorkflowService(AppDbContext db, MissionValidationService validation)
{
    public async Task<Mission> SavePlanAsync(Guid actor, CreateMissionRequest request, Guid? id, CancellationToken ct)
    {
        var operationId = request.OperationId ?? Guid.NewGuid();
        var creationId = Guid.NewGuid();
        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
            var committed = await db.MissionUpdateRequests.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==operationId,ct);
            if(committed is not null)
            {
                if(committed.ActorId!=actor || id.HasValue && committed.MissionId!=id) throw new InvalidOperationException("Operation identity belongs to a different request.");
                var saved=await db.Missions.Include(x=>x.Waypoints).SingleAsync(x=>x.Id==committed.MissionId,ct);
                var submitted=MissionPlans.Propose(saved,request);
                if(MissionValidationService.Hash(submitted)!=MissionValidationService.Hash(MissionPlans.Propose(saved,committed))) throw new InvalidOperationException("Operation identity was reused with different content.");
                await tx.CommitAsync(ct);return saved;
            }
            Mission current;
            if(id.HasValue)
            {
                current=await db.Missions.Include(x=>x.Waypoints).SingleOrDefaultAsync(x=>x.Id==id,ct)??throw new KeyNotFoundException("Mission not found.");
                if(current.CreatedBy!=actor) throw new UnauthorizedAccessException("You may only edit your own mission.");
                if(current.Status is not ("Draft" or "Rejected" or "Pending" or "Pending Review" or "Submitted" or "Approved")) throw new InvalidOperationException("This mission cannot be edited in its current state.");
            }
            else current=new Mission {Id=creationId,CreatedBy=actor,CreatedAt=DateTime.UtcNow,Status="Pending Review"};
            var proposal=MissionPlans.Propose(current,request);
            var result=await validation.EvaluateAsync(proposal,id.HasValue?"Update":"Creation",ct);
            result.UpdateRequestId=result.IsValid?operationId:null;
            validation.Store(proposal,actor,result,id.HasValue||result.IsValid);
            if(!result.IsValid) {await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);throw new MissionValidationException(result);}
            if(!id.HasValue) {current=proposal;current.Status="Pending Review";db.Missions.Add(current);}
            // A newer proposal supersedes the old request, never the current mission.
            await db.MissionUpdateRequests.Where(x=>x.MissionId==current.Id && x.Status=="Pending").ExecuteUpdateAsync(s=>s.SetProperty(x=>x.Status,"Superseded"),ct);
            db.MissionUpdateRequests.Add(new MissionUpdateRequest {Id=operationId,MissionId=current.Id,ActorId=actor,IsInitial=!id.HasValue,BasePlanHash=MissionValidationService.Hash(current),BasePlanJson=System.Text.Json.JsonSerializer.Serialize(MissionPlans.Dto(current),MissionValidationService.JsonOptions),ProposedPlanJson=System.Text.Json.JsonSerializer.Serialize(MissionPlans.Dto(proposal),MissionValidationService.JsonOptions),ValidationRunId=result.Id,CreatedAt=DateTimeOffset.UtcNow});
            await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);return current;
        });
    }

    public async Task CancelAsync(Guid id,Guid actor,bool manager,string reason,CancellationToken ct)
    {
        if(string.IsNullOrWhiteSpace(reason))throw new ArgumentException("Cancellation reason is required.");
        var eventId=Guid.NewGuid();
        await db.Database.CreateExecutionStrategy().ExecuteAsync(async()=>
        {
            db.ChangeTracker.Clear();await using var tx=await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable,ct);
            if(await db.MissionUpdateRequests.AnyAsync(x=>x.Id==eventId,ct)){await tx.CommitAsync(ct);return;}
            var m=await db.Missions.Include(x=>x.Waypoints).SingleOrDefaultAsync(x=>x.Id==id,ct)??throw new KeyNotFoundException("Mission not found.");
            if(!manager&&m.CreatedBy!=actor)throw new UnauthorizedAccessException("You may only cancel your own mission.");
            if(m.Status is "InProgress" or "In Progress" or "Completed")throw new InvalidOperationException("Only pre-flight missions can be cancelled.");
            m.Status="Cancelled";
            await db.MissionUpdateRequests.Where(x=>x.MissionId==id&&x.Status=="Pending").ExecuteUpdateAsync(s=>s.SetProperty(x=>x.Status,"Cancelled").SetProperty(x=>x.Reason,reason.Trim()).SetProperty(x=>x.ReviewedBy,actor).SetProperty(x=>x.ReviewedAt,DateTimeOffset.UtcNow),ct);
            db.MissionUpdateRequests.Add(new MissionUpdateRequest {Id=eventId,MissionId=id,ActorId=actor,Status="Cancelled",Reason=reason.Trim(),CreatedAt=DateTimeOffset.UtcNow,BasePlanHash=MissionValidationService.Hash(m),BasePlanJson=System.Text.Json.JsonSerializer.Serialize(MissionPlans.Dto(m),MissionValidationService.JsonOptions),ProposedPlanJson=System.Text.Json.JsonSerializer.Serialize(MissionPlans.Dto(m),MissionValidationService.JsonOptions)});
            await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);
        });
    }

    public async Task<MissionValidationResult> StartAsync(Guid id, Guid actor, bool manager, CancellationToken ct)
    {
        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
            var m = await db.Missions.Include(x => x.Waypoints).SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new KeyNotFoundException("Mission not found.");
            if (!manager && m.CreatedBy != actor) throw new UnauthorizedAccessException("You may only start your own mission.");
            if (m.Status != "Approved") throw new InvalidOperationException("Only an approved mission can start.");
            if (m.StartTime > DateTime.UtcNow || m.EndTime <= DateTime.UtcNow) throw new InvalidOperationException("Mission start must be within its scheduled flight window.");
            var result = await validation.EvaluateAsync(m, "Preflight", ct); validation.Store(m, actor, result);
            await validation.SyncPreflightAlertAsync(id, result, ct);
            if (result.IsValid) m.Status = "InProgress";
            await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
            if (!result.IsValid) throw new MissionValidationException(result);
            return result;
        });
    }
}
