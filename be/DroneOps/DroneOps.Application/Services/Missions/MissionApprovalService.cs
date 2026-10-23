using DroneOps.Application.DTOs.Request.Missions;
using DroneOps.Application.DTOs.Response.Missions;
using DroneOps.Application.Interfaces.Missions;
using DroneOps.Application.Interfaces.Notifications;
using DroneOps.Domain.Entities;
using DroneOps.Persistence.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
namespace DroneOps.Application.Services.Missions;

public class MissionApprovalService(
    AppDbContext db,
    MissionValidationService validation,
    INotificationService? notificationService = null,
    ILogger<MissionApprovalService>? logger = null) : IMissionApprovalService
{
    public async Task<PagedPendingApprovalsResponse> GetPendingApprovalsAsync(GetPendingApprovalsRequest filter,Guid managerId,CancellationToken ct=default)
    {
        var query=from request in db.MissionUpdateRequests.AsNoTracking()
                  join mission in db.Missions.AsNoTracking() on request.MissionId equals mission.Id
                  where request.Status=="Pending"&&mission.Status!="Cancelled"&&mission.Status!="Completed"&&mission.Status!="InProgress"&&mission.Status!="In Progress"
                  select new {request,mission};
        if(!string.IsNullOrWhiteSpace(filter.Search))query=query.Where(x=>x.mission.Name.Contains(filter.Search)||x.mission.Creator!.FullName.Contains(filter.Search));
        if(filter.UavId.HasValue)query=query.Where(x=>x.mission.UAVId==filter.UavId);
        if(filter.FromDate.HasValue)query=query.Where(x=>x.mission.CreatedAt>=filter.FromDate);
        if(filter.ToDate.HasValue)query=query.Where(x=>x.mission.CreatedAt<=filter.ToDate);
        var total=await query.CountAsync(ct);var page=Math.Max(1,filter.PageNumber);var size=Math.Clamp(filter.PageSize,1,100);
        var rows=await query.OrderBy(x=>x.request.CreatedAt).Skip((page-1)*size).Take(size).ToListAsync(ct);
        var items=new List<PendingApprovalItemResponse>();
        foreach(var row in rows)
        {
            var current=await db.Missions.AsNoTracking().Include(x=>x.Waypoints).Include(x=>x.Creator).Include(x=>x.UAV).SingleAsync(x=>x.Id==row.mission.Id,ct);
            var proposed=MissionPlans.Propose(current,row.request);
            var evidence=await validation.ReadAsync(current.Id,managerId,true,row.request.Id,ct);
            items.Add(new PendingApprovalItemResponse {MissionId=current.Id,UpdateRequestId=row.request.Id,MissionName=proposed.Name,Description=proposed.Description,Status=current.Status,CreatedAt=row.request.CreatedAt.UtcDateTime,StartTime=proposed.StartTime,EndTime=proposed.EndTime,PilotId=current.CreatedBy,PilotName=current.Creator?.FullName,PilotEmail=current.Creator?.Email,UavId=proposed.UAVId,UavCode=current.UAV?.Code,UavName=current.UAV?.Name,WaypointCount=proposed.Waypoints.Count,Validation=evidence,ValidationStatus=evidence?.Status??"Incomplete",ValidationIssues=evidence?.Issues??["Revalidate this request."],CurrentPlan=MissionPlans.Dto(current),ProposedPlan=MissionPlans.Dto(proposed)});
        }
        return new(){Items=items,TotalCount=total,PageNumber=page,PageSize=size};
    }
    public async Task<MissionDecisionResponse> ApproveMissionAsync(Guid id,ApproveMissionRequest request,Guid manager,CancellationToken ct=default)
    {
        var response=await DecideAsync(id,request.UpdateRequestId,request.ValidationRunId,manager,"Approved",request.Comment,ct);
        await TryPublishDecisionNotificationAsync(response,manager,true,ct);
        return response;
    }
    public async Task<MissionDecisionResponse> RejectMissionAsync(Guid id,RejectMissionRequest request,Guid manager,CancellationToken ct=default)
    {
        if(string.IsNullOrWhiteSpace(request.Reason))throw new ArgumentException("Rejection reason is mandatory.");
        var response=await DecideAsync(id,request.UpdateRequestId,null,manager,"Rejected",request.Reason.Trim(),ct);
        await TryPublishDecisionNotificationAsync(response,manager,false,ct);
        return response;
    }
    private async Task<MissionDecisionResponse> DecideAsync(Guid id,Guid? requestId,Guid? runId,Guid manager,string decision,string? comment,CancellationToken ct)
    {
        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async()=>
        {
            db.ChangeTracker.Clear();await using var tx=await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable,ct);
            var m=await db.Missions.Include(x=>x.Waypoints).SingleOrDefaultAsync(x=>x.Id==id,ct)??throw new KeyNotFoundException("Mission not found.");
            if(m.CreatedBy==manager)throw new InvalidOperationException("Self-approval is forbidden.");
            var request=requestId.HasValue?await db.MissionUpdateRequests.SingleOrDefaultAsync(x=>x.Id==requestId&&x.MissionId==id,ct):await db.MissionUpdateRequests.Where(x=>x.MissionId==id&&x.Status=="Pending").SingleOrDefaultAsync(ct);
            if(request is null&&!requestId.HasValue)
            {
                var last=await db.MissionApprovals.AsNoTracking().Where(x=>x.MissionId==id&&x.UserId==manager&&x.Status==decision).OrderByDescending(x=>x.ApprovedAt).FirstOrDefaultAsync(ct);
                if(last is not null){await tx.CommitAsync(ct);return Response(last,m.Status,true);}
            }
            if(request is null)throw new KeyNotFoundException("Review request not found.");
            var log=await db.MissionApprovals.AsNoTracking().SingleOrDefaultAsync(x=>x.UpdateRequestId==request.Id,ct);
            if(log is not null&&log.UserId==manager&&log.Status==decision){await tx.CommitAsync(ct);return Response(log,m.Status,true);}
            if(request.Status!="Pending")throw new InvalidOperationException("This request is stale, superseded, cancelled or already reviewed.");
            if(!requestId.HasValue&&!request.IsInitial)throw new InvalidOperationException("UpdateRequestId is required to review a proposed update.");
            if(m.Status is "Cancelled" or "Completed" or "InProgress" or "In Progress")throw new InvalidOperationException("Mission is no longer eligible for review.");
            if(request.BasePlanHash!=""&&request.BasePlanHash!=MissionValidationService.Hash(m))throw new InvalidOperationException("The current mission changed since this proposal. Submit a new request.");
            var proposal=MissionPlans.Propose(m,request);
            if(decision=="Approved")
            {
                if(runId.HasValue)
                {
                    var reviewed=await db.MissionValidationRuns.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==runId,ct);
                    if(reviewed is null||reviewed.UpdateRequestId!=request.Id||reviewed.PlanHash!=MissionValidationService.Hash(proposal))throw new InvalidOperationException("Reviewed validation does not belong to this request/plan.");
                }
                var fresh=await validation.EvaluateAsync(proposal,"Approval",ct);fresh.UpdateRequestId=request.Id;validation.Store(proposal,manager,fresh);request.ValidationRunId=fresh.Id;runId=fresh.Id;
                if(!fresh.IsValid){await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);throw new MissionValidationException(fresh);}
                // Apply only after approval, to the same mission, in the decision transaction.
                db.Waypoints.RemoveRange(m.Waypoints);db.Waypoints.AddRange(proposal.Waypoints);m.Name=proposal.Name;m.Description=proposal.Description;m.UAVId=proposal.UAVId;m.AltitudeReference=proposal.AltitudeReference;m.StartTime=proposal.StartTime;m.EndTime=proposal.EndTime;m.Waypoints=proposal.Waypoints;
            }
            var previous=m.Status;
            if(decision=="Approved")m.Status="Approved";else if(m.Status!="Approved")m.Status="Rejected";
            request.Status=decision;request.ReviewedBy=manager;request.ReviewedAt=DateTimeOffset.UtcNow;request.Reason=comment;
            var audit=new MissionApproval {Id=Guid.NewGuid(),MissionId=id,UpdateRequestId=request.Id,UserId=manager,Status=decision,Comment=comment,ApprovedAt=request.ReviewedAt,ValidationRunId=decision=="Approved"?runId:null};db.MissionApprovals.Add(audit);
            await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);var response=Response(audit,m.Status,false);response.PreviousStatus=previous;return response;
        });
    }
    private static MissionDecisionResponse Response(MissionApproval log,string state,bool replay)=>new(){MissionId=log.MissionId,UpdateRequestId=log.UpdateRequestId,Decision=log.Status,PreviousStatus=state,NewStatus=state,Comment=log.Comment,DecidedAt=log.ApprovedAt??DateTimeOffset.UtcNow,DecidedBy=log.UserId,IdempotentReplay=replay,ValidationRunId=log.ValidationRunId};

    private async Task TryPublishDecisionNotificationAsync(MissionDecisionResponse response,Guid actorUserId,bool approved,CancellationToken ct)
    {
        if(response.IdempotentReplay || notificationService is null)return;
        try { await notificationService.PublishMissionDecisionAsync(response.MissionId,actorUserId,approved,ct); }
        catch(Exception ex) when(!ct.IsCancellationRequested)
        {
            logger?.LogWarning(ex,"Could not publish mission-decision notification for {MissionId}.",response.MissionId);
        }
    }
}
