using System.Text.Json;
using DroneOps.Application.DTOs.Request.Missions;
using DroneOps.Application.DTOs.Response.Missions;
using DroneOps.Application.Services.Missions;
using DroneOps.Domain.Entities;
using DroneOps.Persistence;
using DroneOps.Persistence.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

internal static class MissionApprovalChecks
{
    public sealed class AdvisoryWeather : IMissionWeatherService
    {
        public Task<WeatherAdvisory> GetAsync(Mission m, CancellationToken ct) => Task.FromResult(new WeatherAdvisory {Available=false,Warnings=["Simulated wind/rain/thunderstorm advisory; no live weather used."]});
    }
    private sealed class FixedTerrain(decimal elevationMslMeters) : ITerrainElevationService
    {
        public Task<TerrainElevation> GetElevationAsync(decimal latitude,decimal longitude,CancellationToken ct) =>
            Task.FromResult(new TerrainElevation(elevationMslMeters,"Fixture"));
    }
    private sealed class BrokenTerrain : ITerrainElevationService
    {
        public Task<TerrainElevation> GetElevationAsync(decimal latitude,decimal longitude,CancellationToken ct) =>
            throw new InvalidOperationException("Simulated terrain provider failure");
    }
    public static async Task RunAsync(string root)
    {
        var config=new ConfigurationBuilder().AddJsonFile(Path.Combine(root,"be/DroneOps/DroneOps.API/appsettings.Local.json")).Build();
        await using var source=PostgresDataSourceFactory.Create(config.GetConnectionString("DefaultConnection")!);
        var options=new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(source,o=>o.EnableRetryOnFailure()).Options;
        await using var db=new AppDbContext(options);
        var manager=await db.Users.AsNoTracking().Where(x=>x.Role.Name=="operation_manager").Select(x=>x.Id).FirstAsync();
        var role=await db.Roles.AsNoTracking().Where(x=>x.Name=="uav_operator").Select(x=>x.Id).FirstAsync();
        var uid=Guid.NewGuid();var pid=Guid.NewGuid();var aircraft=Guid.NewGuid();var zone=Guid.NewGuid();var boundary=Guid.NewGuid();
        var weather=new AdvisoryWeather();var validation=new MissionValidationService(db,weather);var workflow=new MissionWorkflowService(db,validation);
        int count=0;
        void Check(bool ok,string message) { if(!ok)throw new Exception("FAIL: "+message);Console.WriteLine("PASS: "+message);count++; }
        CreateMissionRequest Plan() => new() { UavId=aircraft,Name="Temporary mission workflow test",StartTime=DateTime.UtcNow.AddDays(1),EndTime=DateTime.UtcNow.AddDays(1).AddHours(1),Waypoints=[new() {SequenceOrder=1,Latitude=-45,Longitude=-120,Altitude=50},new() {SequenceOrder=2,Latitude=-44.999m,Longitude=-119.999m,Altitude=50}] };
        try
        {
            var today=DateOnly.FromDateTime(DateTime.UtcNow);
            db.Users.Add(new User {Id=uid,RoleId=role,FullName="Temporary mission validation operator",Email=$"mission-{uid:N}@example.com",PasswordHash="test-disabled",CreatedAt=DateTimeOffset.UtcNow});
            db.Pilots.Add(new Pilot {Id=pid,UserId=uid,PilotLicenseNo="TEST",LicenseIssuedDate=today.AddYears(-1),LicenseExpiredDate=today.AddYears(1),CreatedAt=DateTime.UtcNow});
            db.UAVs.Add(new UAV {Id=aircraft,PilotId=pid,Code=$"TEST-{aircraft:N}",Name="Workflow fixture",Status="Active",CreatedAt=DateTimeOffset.UtcNow});
            db.Geofences.Add(new Geofence {Id=zone,Name=$"Workflow-{zone:N}",Coordinates="[{\"latitude\":9,\"longitude\":105},{\"latitude\":11,\"longitude\":105},{\"latitude\":11,\"longitude\":107},{\"latitude\":9,\"longitude\":107}]",CreatedAt=DateTime.UtcNow});
            db.Geofences.Add(new Geofence {Id=boundary,RestrictionType="OperationalBoundary",VerticalRule="AllAltitudes",Name=$"Boundary-{boundary:N}",Coordinates="[{\"latitude\":-46,\"longitude\":-121},{\"latitude\":-44,\"longitude\":-121},{\"latitude\":-44,\"longitude\":-119},{\"latitude\":-46,\"longitude\":-119}]",CreatedAt=DateTime.UtcNow});
            await db.SaveChangesAsync();db.ChangeTracker.Clear();
            var bad=Plan();bad.Waypoints=[];
            try {await workflow.SavePlanAsync(uid,bad,null,default);throw new Exception("Invalid creation accepted");}
            catch(MissionValidationException e) {Check(e.Result.Issues.Count>0,"invalid creation returns findings");}
            Check(!await db.Missions.AnyAsync(x=>x.CreatedBy==uid),"failed creation never enters manager queue");
            Check(await db.MissionValidationRuns.AnyAsync(x=>x.ActorId==uid && !x.IsValid && x.MissionId==null),"failed creation result is persisted");
            var restricted=Plan();restricted.Waypoints=[new(){SequenceOrder=1,Latitude=10,Longitude=104,Altitude=50},new(){SequenceOrder=2,Latitude=10,Longitude=108,Altitude=50}];
            try {await workflow.SavePlanAsync(uid,restricted,null,default);throw new Exception("Restricted route accepted");}
            catch(MissionValidationException e) {Check(e.Result.Findings.Count>0,"creation blocks a route intersecting configured restricted airspace");}
            var mission=await workflow.SavePlanAsync(uid,Plan(),null,default);
            Check(mission.Status=="Pending Review","valid creation automatically enters Pending Review");
            var latest=(await validation.PendingAsync(mission.Id,default))!.Validation;
            Check(latest!.IsValid && latest.Warnings.Count>0,"weather unavailability/warnings never block mandatory validation");
            var storedRun=await db.MissionValidationRuns.AsNoTracking().SingleAsync(x=>x.Id==latest.Id);
            Check(storedRun.ResultStatus=="Pass"&&storedRun.PlanHash.Length==64&&storedRun.RestrictionRevision==latest.RestrictionRevision&&storedRun.EvidenceJson.Contains("planHash"),"validation audit stores outcome, payload identity, restriction revision and evidence");
            var outsideBoundary=await db.Missions.AsNoTracking().Include(x=>x.Waypoints).SingleAsync(x=>x.Id==mission.Id);
            foreach(var point in outsideBoundary.Waypoints)point.Latitude=-40;
            Check((await validation.EvaluateAsync(outsideBoundary,"Manager",default)).Findings.Any(x=>x.Rule=="OutsideOperationalBoundary"),"route and segments must remain inside an operational boundary");
            Check((await new MissionApprovalService(db,validation).GetPendingApprovalsAsync(new(),manager)).Items.Any(x=>x.MissionId==mission.Id && x.Validation!.Id==latest.Id),"manager queue returns persisted result");
            db.ChangeTracker.Clear();
            await db.UAVs.Where(x=>x.Id==aircraft).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.Status,"Inactive"));
            var recheck=await validation.RevalidateAsync(mission.Id,manager,true,"Manager",default);
            Check(!recheck.IsValid,"manager revalidation catches newly inactive UAV");
            var approval=new MissionApprovalService(db,validation);
            try {await approval.ApproveMissionAsync(mission.Id,new(){ExpectedStatus="Pending Review"},manager);throw new Exception("Invalid approval accepted");}
            catch(MissionValidationException) {Check(true,"approve independently rechecks and rejects invalid mission");}
            Check(!await db.MissionApprovals.AnyAsync(x=>x.MissionId==mission.Id),"failed approval does not write decision audit");
            await db.UAVs.Where(x=>x.Id==aircraft).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.Status,"Active"));
            var approved=await approval.ApproveMissionAsync(mission.Id,new(){ExpectedStatus="Pending Review"},manager);
            Check(approved.NewStatus=="Approved" && approved.ValidationRunId.HasValue,"approval stores fresh validation and decision atomically");
            var replay=await approval.ApproveMissionAsync(mission.Id,new(){ExpectedStatus="Pending Review"},manager);
            Check(replay.IdempotentReplay && replay.ValidationRunId==approved.ValidationRunId && await db.MissionApprovals.CountAsync(x=>x.MissionId==mission.Id)==1,"approval retry does not duplicate decision");
            var original=await db.Missions.AsNoTracking().Include(x=>x.Waypoints).SingleAsync(x=>x.Id==mission.Id);
            var originalHash=MissionValidationService.Hash(original);
            var edit=Plan();edit.Name="Proposed approved-mission update";edit.StartTime=original.StartTime!.Value;edit.EndTime=original.EndTime!.Value;
            await workflow.SavePlanAsync(uid,edit,mission.Id,default);
            var update=(await validation.PendingAsync(mission.Id,default))!;
            var unchanged=await db.Missions.AsNoTracking().Include(x=>x.Waypoints).SingleAsync(x=>x.Id==mission.Id);
            Check(unchanged.Status=="Approved"&&MissionValidationService.Hash(unchanged)==originalHash,"editing an approved mission creates a proposal without changing current content/status");
            var queue=await approval.GetPendingApprovalsAsync(new(),manager);
            Check(queue.Items.Any(x=>x.MissionId==mission.Id&&x.UpdateRequestId==update.Id&&x.Status=="Approved"&&x.ProposedPlan!.Name==edit.Name),"manager queue includes approved missions with independent pending updates");
            await approval.RejectMissionAsync(mission.Id,new(){UpdateRequestId=update.Id,Reason="Keep approved plan"},manager);
            unchanged=await db.Missions.AsNoTracking().Include(x=>x.Waypoints).SingleAsync(x=>x.Id==mission.Id);
            Check(unchanged.Status=="Approved"&&MissionValidationService.Hash(unchanged)==originalHash,"rejecting an update preserves approved mission data and status");
            edit.Name="Apply approved-mission update";await workflow.SavePlanAsync(uid,edit,mission.Id,default);
            update=(await validation.PendingAsync(mission.Id,default))!;
            await approval.ApproveMissionAsync(mission.Id,new(){UpdateRequestId=update.Id,ValidationRunId=update.Validation!.Id},manager);
            Check(await db.Missions.AsNoTracking().AnyAsync(x=>x.Id==mission.Id&&x.Name==edit.Name&&x.Status=="Approved"),"approval atomically applies proposed content to the same mission");
            var historical=await validation.HistoryAsync(mission.Id,uid,false,default);
            Check(historical.Any(x=>x.Id==update.Id&&x.ReviewedBy==manager)&&historical.Any(x=>x.Status=="Rejected"),"history preserves separate proposals and immutable reviewer/decision evidence");
            Check((await validation.ReadAsync(mission.Id,uid,false,update.Id,default))?.UpdateRequestId==update.Id,"validation lookup is scoped to updateRequestId");
            edit.Name="Superseded proposal";await workflow.SavePlanAsync(uid,edit,mission.Id,default);var superseded=(await validation.PendingAsync(mission.Id,default))!;
            edit.Name="Latest proposal";await workflow.SavePlanAsync(uid,edit,mission.Id,default);var latestUpdate=(await validation.PendingAsync(mission.Id,default))!;
            try {await approval.ApproveMissionAsync(mission.Id,new(){UpdateRequestId=superseded.Id},manager);throw new Exception("Superseded approval accepted");}
            catch(InvalidOperationException){Check(true,"superseded review requests cannot overwrite the current mission");}
            await approval.RejectMissionAsync(mission.Id,new(){UpdateRequestId=latestUpdate.Id,Reason="Test"},manager);
            var queryWeather=new MissionWeatherQueryService(db,weather,Microsoft.Extensions.Options.Options.Create(new MissionWeatherOptions()));
            Check((await queryWeather.ReadAsync(mission.Id,uid,false,update.Id,false,default)).UpdateRequestId==update.Id,"weather for a proposal retains request identity");
            try {await queryWeather.ReadAsync(mission.Id,Guid.NewGuid(),false,null,false,default);throw new Exception("Weather scope leak");}
            catch(UnauthorizedAccessException){Check(true,"weather endpoint enforces mission ownership");}
            // Altitude-aware rules apply only to the horizontal intersection portion.
            var verticalZone=await db.Geofences.SingleAsync(x=>x.Id==zone);
            verticalZone.Coordinates="[{\"latitude\":-46,\"longitude\":-121},{\"latitude\":-44,\"longitude\":-121},{\"latitude\":-44,\"longitude\":-119},{\"latitude\":-46,\"longitude\":-119}]";
            verticalZone.MinAltitudeMeters=100;verticalZone.MaxAltitudeMeters=200;verticalZone.AltitudeReference="MSL";
            await db.SaveChangesAsync();db.ChangeTracker.Clear();
            var forAltitude=await db.Missions.AsNoTracking().Include(x=>x.Waypoints).SingleAsync(x=>x.Id==mission.Id);forAltitude.AltitudeReference="MSL";
            var below=await validation.EvaluateAsync(forAltitude,"Manager",default);
            Check(below.IsValid,"route below a matching altitude-referenced restriction passes");
            foreach(var point in forAltitude.Waypoints)point.Altitude=150;
            var insideAltitude=await validation.EvaluateAsync(forAltitude,"Manager",default);
            Check(!insideAltitude.IsValid&&insideAltitude.Findings.Any(x=>x.Rule=="AirspaceAltitudeIntersection"),"restricted airspace takes precedence inside an operational boundary");
            forAltitude.AltitudeReference="AGL";
            Check((await validation.EvaluateAsync(forAltitude,"Manager",default)).Status=="Incomplete","incompatible altitude reference is incomplete, never assumed converted");
            foreach(var point in forAltitude.Waypoints)point.Altitude=50;
            var converted=await new MissionValidationService(db,weather,new FixedTerrain(100)).EvaluateAsync(forAltitude,"Manager",default);
            Check(converted.Status=="Fail"&&converted.Findings.Count>0,"terrain fixture converts AGL to MSL and applies the altitude band");
            Check(converted.RestrictionEvidence.Any(x=>x.GeofenceId==zone&&x.Revision>0&&x.CoordinatesJson.Contains("latitude")&&!string.IsNullOrWhiteSpace(x.Text)),"intersected restriction retains revisioned map and text evidence");
            var brokenValidation=new MissionValidationService(db,weather,new BrokenTerrain());
            var errored=await brokenValidation.EvaluateAsync(forAltitude,"Manager",default);
            brokenValidation.Store(forAltitude,uid,errored);await db.SaveChangesAsync();
            Check(errored.Outcome=="Error"&&await db.MissionValidationRuns.AsNoTracking().AnyAsync(x=>x.Id==errored.Id&&x.ResultStatus=="Error"),"terrain processing failure is persisted as Error, never Pass");
            Check(await db.GeofenceRevisions.CountAsync(x=>x.GeofenceId==zone)>=2,"geofence updates retain immutable restriction revisions");
            Check((await validation.ReadAsync(mission.Id,uid,false,update.Id,default))!.Stale,"changing restrictions invalidates stored validation evidence");
            // Restore the fixture zone away from the mission for remaining preflight tests.
            await db.Geofences.Where(x=>x.Id==zone).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.Coordinates,"[{\"latitude\":9,\"longitude\":105},{\"latitude\":11,\"longitude\":105},{\"latitude\":11,\"longitude\":107},{\"latitude\":9,\"longitude\":107}]").SetProperty(x=>x.MinAltitudeMeters,(decimal?)null).SetProperty(x=>x.MaxAltitudeMeters,(decimal?)null).SetProperty(x=>x.AltitudeReference,(string?)null));
            await db.UAVs.Where(x=>x.Id==aircraft).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.Status,"Inactive"));
            Check(!await MissionPreflightMonitor.DueMissions(db,DateTime.UtcNow).AnyAsync(x=>x.Id==mission.Id),"scheduler excludes flights more than 15 minutes away");
            await db.Missions.Where(x=>x.Id==mission.Id).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.StartTime,DateTime.UtcNow.AddMinutes(14)));
            var dueIds=await MissionPreflightMonitor.DueMissions(db,DateTime.UtcNow).Select(x=>x.Id).Take(100).ToListAsync();
            Check(dueIds.Contains(mission.Id),"scheduler selects approved missions within 15 minutes of departure");
            var preflight=await validation.RevalidateAsync(mission.Id,uid,true,"Preflight",default);
            Check(!await MissionPreflightMonitor.DueMissions(db,DateTime.UtcNow).AnyAsync(x=>x.Id==mission.Id),"scheduler avoids immediately repeating a completed preflight check");
            Check(preflight.Status=="Fail","preflight stores Fail after conditions change");
            Check(await db.Incidents.AnyAsync(x=>x.MissionId==mission.Id && x.Type=="PreflightValidation"),"preflight produces an operator/manager incident alert");
            Check(await db.Missions.AsNoTracking().Where(x=>x.Id==mission.Id).Select(x=>x.Status).SingleAsync()=="Approved","blocked readiness preserves approval history/status");
            await db.Missions.Where(x=>x.Id==mission.Id).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.StartTime,DateTime.UtcNow.AddMinutes(-1)));
            try {await workflow.StartAsync(mission.Id,uid,false,default);throw new Exception("Blocked start accepted");}
            catch(MissionValidationException) {Check(true,"start endpoint prevents starting blocked mission");}
            await db.UAVs.Where(x=>x.Id==aircraft).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.Status,"Active"));
            var started=await workflow.StartAsync(mission.Id,uid,false,default);
            Check(started.IsValid && await db.Missions.AsNoTracking().Where(x=>x.Id==mission.Id).Select(x=>x.Status).SingleAsync()=="InProgress","fresh passing preflight allows internal mission start despite advisory weather");
            var rejected=await workflow.SavePlanAsync(uid,new CreateMissionRequest {UavId=aircraft,Name="Reject fixture",StartTime=DateTime.UtcNow.AddDays(2),EndTime=DateTime.UtcNow.AddDays(2).AddHours(1),Waypoints=Plan().Waypoints},null,default);
            Check((await approval.RejectMissionAsync(rejected.Id,new(){Reason="Test findings"},manager)).NewStatus=="Rejected","reject workflow remains available");
            try {await validation.RevalidateAsync(rejected.Id,manager,false,"Manager",default);throw new Exception("Other operator allowed");}
            catch(UnauthorizedAccessException) {Check(true,"other operator cannot revalidate someone else's mission");}
            List<MissionRouteGeometry.Point> box(double left,double right)=>[new(0,left),new(1,left),new(1,right),new(0,right)];
            Check(MissionRouteGeometry.Intersects([new(.5,-1),new(.5,2)],box(0,1)),"segment crossing prohibited airspace is blocked even with endpoints outside");
            Check(!MissionRouteGeometry.Intersects([new(2,-1),new(2,2)],box(0,1)),"route outside prohibited airspace passes the intersection check");
            Check(MissionRouteGeometry.Intersects([new(0,-1),new(0,2)],box(0,1)),"touching a restricted boundary is detected");
            var concurrentPlan=Plan();concurrentPlan.StartTime=DateTime.UtcNow.AddDays(3);concurrentPlan.EndTime=concurrentPlan.StartTime.AddHours(1);
            var concurrent=await workflow.SavePlanAsync(uid,concurrentPlan,null,default);
            async Task<MissionDecisionResponse> ConcurrentDecision()
            {
                await using var context=new AppDbContext(options);
                return await new MissionApprovalService(context,new MissionValidationService(context,weather)).ApproveMissionAsync(concurrent.Id,new(){ExpectedStatus="Pending Review"},manager);
            }
            var decisions=await Task.WhenAll(ConcurrentDecision(),ConcurrentDecision());
            Check(decisions.Count(x=>x.IdempotentReplay)==1 && await db.MissionApprovals.CountAsync(x=>x.MissionId==concurrent.Id)==1,"concurrent approval produces one decision and one replay");
            Console.WriteLine($"{count} PostgreSQL workflow checks passed. Weather is simulated.");
        }
        finally
        {
            db.ChangeTracker.Clear();var missions=db.Missions.Where(x=>x.CreatedBy==uid).Select(x=>x.Id);
            await db.Incidents.Where(x=>missions.Contains(x.MissionId)).ExecuteDeleteAsync();
            await db.MissionApprovals.Where(x=>missions.Contains(x.MissionId)).ExecuteDeleteAsync();
            await db.Waypoints.Where(x=>missions.Contains(x.MissionId)).ExecuteDeleteAsync();
            await db.MissionValidationRuns.Where(x=>x.ActorId==uid || x.MissionId.HasValue&&missions.Contains(x.MissionId.Value)).ExecuteDeleteAsync();
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM \"MissionWeatherCache\" WHERE \"ForecastJson\"->>'missionId' IN (SELECT \"Id\"::text FROM \"Mission\" WHERE \"CreatedBy\"={uid})");
            await db.MissionUpdateRequests.Where(x=>missions.Contains(x.MissionId)).ExecuteDeleteAsync();
            await db.Missions.Where(x=>x.CreatedBy==uid).ExecuteDeleteAsync();
            await db.UAVs.Where(x=>x.Id==aircraft).ExecuteDeleteAsync();await db.Pilots.Where(x=>x.Id==pid).ExecuteDeleteAsync();await db.Users.Where(x=>x.Id==uid).ExecuteDeleteAsync();await db.Geofences.Where(x=>x.Id==zone||x.Id==boundary).ExecuteDeleteAsync();await db.GeofenceRevisions.Where(x=>x.GeofenceId==zone||x.GeofenceId==boundary).ExecuteDeleteAsync();
            Console.WriteLine("Temporary workflow records removed.");
        }
    }
}
