using System.Text.Json;
using DroneOps.Application.DTOs.Request.PostFlight;
using DroneOps.Application.Services.PostFlight;
using DroneOps.Domain.Entities;
using DroneOps.Persistence;
using DroneOps.Persistence.Data;
using DroneOps.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

internal static class PostFlightPostgresChecks
{
    public static async Task RunAsync(string root)
    {
        var settings=JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(root,"be/DroneOps/DroneOps.API/appsettings.Local.json")));
        var connection=settings.RootElement.GetProperty("ConnectionStrings").GetProperty("DefaultConnection").GetString()!;
        await using var source=PostgresDataSourceFactory.Create(connection);
        await PostFlightSchema.CheckAsync(source);
        var options=new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(source,o=>o.EnableRetryOnFailure()).Options;
        await using var db=new AppDbContext(options);
        var repository=new PostFlightRepository(db);var service=new PostFlightService(repository);
        var role=await db.Roles.Where(x=>x.Name=="uav_operator").Select(x=>x.Id).FirstAsync();
        var owner=Guid.NewGuid();var outsider=Guid.NewGuid();var pilot=Guid.NewGuid();var uav=Guid.NewGuid();
        var concurrentMission=Guid.NewGuid();var failureMission=Guid.NewGuid();var approvedMission=Guid.NewGuid();var disarmMission=Guid.NewGuid();var secondMission=Guid.NewGuid();
        var start=DateTimeOffset.UtcNow.AddMinutes(-20);var completedAt=start.AddMinutes(10);
        var fixtureMissions=new[]{concurrentMission,failureMission,approvedMission,disarmMission,secondMission};
        var passed=0;
        void Check(bool condition,string label){if(!condition)throw new Exception("FAIL: "+label);Console.WriteLine("PASS: "+label);passed++;}
        Mission Flight(Guid id,string status)=>new(){Id=id,CreatedBy=owner,UAVId=uav,Name="Lifecycle "+id.ToString("N"),Status=status,StartTime=start.UtcDateTime,EndTime=start.AddHours(1).UtcDateTime,CreatedAt=start.AddHours(-1).UtcDateTime};
        FlightCompletionEvent Completion(Guid mission,string key,DateTimeOffset at)=>new(){Id=Guid.NewGuid(),MissionId=mission,UAVId=uav,SourceId="trusted-lifecycle-fixture",IdempotencyKey=key,EventType="MISSION_COMPLETED",CompletedAt=at,ReceivedAt=DateTimeOffset.UtcNow,IsTrusted=true,Evidence="{\"fixture\":true}"};
        PostFlightReport Report(Guid mission)=>new(){Id=Guid.NewGuid(),MissionId=mission,Revision=1,IsCurrent=true,GenerationStatus="Queued",Status="Completed",CoveredTelemetryFrom=start,CoveredTelemetryThrough=completedAt,CreatedAt=DateTimeOffset.UtcNow,GenerationReason="PostgreSQL concurrency fixture"};
        TelemetryRecord Sample(Guid mission,int seconds)=>new(){Id=Guid.NewGuid(),MissionId=mission,UAVId=uav,SourceId="lifecycle-fixture",SourceTimestamp=start.AddSeconds(seconds),ReceivedAt=start.AddSeconds(seconds),RecordedAt=start.AddSeconds(seconds),Latitude=10m+seconds/100000m,Longitude=106m+seconds/100000m,Altitude=20,Speed=5,BatteryLevel=80,IsValid=true};
        async Task Denied(Func<Task> action,string label){try{await action();throw new Exception("Expected rejection");}catch(UnauthorizedAccessException){Check(true,label);}}
        try
        {
            db.Users.AddRange(
                new User{Id=owner,RoleId=role,FullName="Lifecycle owner",Email=$"lifecycle-{owner:N}@test.invalid",PasswordHash="fixture",CreatedAt=DateTimeOffset.UtcNow},
                new User{Id=outsider,RoleId=role,FullName="Lifecycle outsider",Email=$"lifecycle-{outsider:N}@test.invalid",PasswordHash="fixture",CreatedAt=DateTimeOffset.UtcNow});
            db.Pilots.Add(new Pilot{Id=pilot,UserId=owner,PilotLicenseNo="LIFECYCLE",CreatedAt=DateTime.UtcNow});
            db.UAVs.Add(new UAV{Id=uav,PilotId=pilot,Code=$"LIFE-{uav:N}",Name="Lifecycle UAV",Status="Active",CreatedAt=DateTimeOffset.UtcNow});
            db.Missions.AddRange(Flight(concurrentMission,"InProgress"),Flight(failureMission,"InProgress"),Flight(approvedMission,"Approved"),Flight(disarmMission,"InProgress"),Flight(secondMission,"InProgress"));
            db.TelemetryRecords.AddRange(Sample(concurrentMission,0),Sample(concurrentMission,30),Sample(disarmMission,0),Sample(disarmMission,30));
            await db.SaveChangesAsync();db.ChangeTracker.Clear();

            // Bypass the process-local service gate: separate DbContexts exercise the
            // PostgreSQL advisory/key lock, mission row lock and unique constraints.
            var claims=await Task.WhenAll(new[]{"concurrent-a","concurrent-b"}.Select(async key=>
            {
                await using var context=new AppDbContext(options);
                return await new PostFlightRepository(context).ClaimCompletionAsync(
                    Completion(concurrentMission,key,completedAt),Report(concurrentMission),
                    new[]{"InProgress","In Progress","Active","Flying","Executing"});
            }));
            Check(claims.Count(x=>x.Outcome=="Created")==1&&claims.Count(x=>x.Outcome=="AlreadyCompleted")==1,
                "concurrent trusted completions atomically create one flight outcome");
            Check(await db.FlightCompletionEvents.CountAsync(x=>x.MissionId==concurrentMission)==1&&
                  await db.PostFlightReports.CountAsync(x=>x.MissionId==concurrentMission)==1&&
                  await db.Missions.Where(x=>x.Id==concurrentMission).Select(x=>x.Status).SingleAsync()=="Completed",
                "database invariants retain exactly one trusted event and revision 1");

            var winner=await db.FlightCompletionEvents.AsNoTracking().SingleAsync(x=>x.MissionId==concurrentMission);
            var replayRequest=new RecordFlightCompletionRequest{MissionId=concurrentMission,UavId=uav,SourceId=winner.SourceId,EventType=winner.EventType,CompletedAt=winner.CompletedAt,Evidence="replay"};
            var replay=await service.RecordTrustedCompletionAsync(winner.IdempotencyKey,replayRequest);
            Check(replay.Revision==1&&await db.FlightCompletionEvents.CountAsync(x=>x.MissionId==concurrentMission)==1,
                "same-key replay returns the existing report without duplication");
            try{await service.RecordTrustedCompletionAsync("ambiguous-key",new(){MissionId=concurrentMission,UavId=uav,SourceId=winner.SourceId,EventType=winner.EventType,CompletedAt=completedAt.AddSeconds(1)});throw new Exception("Ambiguous completion accepted");}
            catch(InvalidOperationException){Check(await db.FlightCompletionEvents.CountAsync(x=>x.MissionId==concurrentMission)==1,"ambiguous completion is rejected without duplicate evidence");}

            try{await service.RecordTrustedCompletionAsync("pre-start",new(){MissionId=approvedMission,UavId=uav,SourceId="trusted-lifecycle-fixture",EventType="MISSION_COMPLETED",CompletedAt=completedAt});throw new Exception("Unstarted mission completed");}
            catch(InvalidOperationException){Check(!await db.FlightCompletionEvents.AnyAsync(x=>x.MissionId==approvedMission)&&!await db.PostFlightReports.AnyAsync(x=>x.MissionId==approvedMission),"completion cannot bypass trusted start and rolls back fully");}
            Check(await db.Missions.Where(x=>x.Id==disarmMission).Select(x=>x.Status).SingleAsync()=="InProgress"&&
                  !await db.FlightCompletionEvents.AnyAsync(x=>x.MissionId==disarmMission),
                "telemetry disconnect/disarm evidence alone never completes a mission");

            await service.ProcessQueuedReportsAsync();
            var available=await db.PostFlightReports.AsNoTracking().SingleAsync(x=>x.MissionId==concurrentMission&&x.IsCurrent);
            Check(available.GenerationStatus=="Available"&&available.CoveredTelemetryFrom.HasValue&&available.CoveredTelemetryThrough.HasValue&&
                  Math.Abs((available.CoveredTelemetryFrom.Value-start).TotalMilliseconds)<1&&
                  Math.Abs((available.CoveredTelemetryThrough.Value-completedAt).TotalMilliseconds)<1,
                "report stores status and immutable telemetry coverage window");
            var winnerEvidence=JsonDocument.Parse(winner.Evidence!).RootElement;
            Check(winnerEvidence.TryGetProperty("execution",out _)
                  && winnerEvidence.GetProperty("suppliedEvidence").GetString()=="{\"fixture\":true}",
                "atomic execution snapshot and supplied trusted evidence are retained");
            await Denied(()=>service.GetReportByMissionIdAsync(concurrentMission,outsider,false),"report authorization rejects an out-of-scope operator");

            var late=Sample(concurrentMission,60);late.ReceivedAt=available.GeneratedAt!.Value.AddSeconds(1);db.TelemetryRecords.Add(late);
            await db.SaveChangesAsync();db.ChangeTracker.Clear();
            await service.ProcessQueuedReportsAsync();
            Check(await db.PostFlightReports.Where(x=>x.MissionId==concurrentMission&&x.IsCurrent).Select(x=>x.GenerationStatus).SingleAsync()=="Outdated",
                "late telemetry marks the current report Outdated");
            await service.RegenerateReportAsync(concurrentMission,new(){Reason="Include verified late telemetry"},owner);
            await service.ProcessQueuedReportsAsync();
            var revisions=await db.PostFlightReports.AsNoTracking().Where(x=>x.MissionId==concurrentMission).OrderBy(x=>x.Revision).ToListAsync();
            Check(revisions.Count==2&&!revisions[0].IsCurrent&&revisions[1].IsCurrent&&revisions[1].Revision==2,
                "regeneration creates a new revision and preserves the old report");

            await service.RecordTrustedCompletionAsync("failure-completion",new(){MissionId=failureMission,UavId=uav,SourceId="trusted-lifecycle-fixture",EventType="FLIGHT_COMPLETED",CompletedAt=completedAt,Evidence="no telemetry fixture"});
            await service.ProcessQueuedReportsAsync();
            var failed=await db.PostFlightReports.AsNoTracking().SingleAsync(x=>x.MissionId==failureMission);
            var failedEvidence=await db.FlightCompletionEvents.AsNoTracking().Where(x=>x.MissionId==failureMission).Select(x=>x.Evidence).SingleAsync();
            Check(failed.GenerationStatus=="Failed"&&!string.IsNullOrWhiteSpace(failed.FailureReason)&&
                  failedEvidence!=null&&JsonDocument.Parse(failedEvidence).RootElement.TryGetProperty("execution",out _),
                "generation failure preserves completion evidence, failed status and reason");
            Console.WriteLine($"{passed} PostgreSQL post-flight lifecycle checks passed.");
        }
        finally
        {
            db.ChangeTracker.Clear();
            await db.TelemetryRecords.Where(x=>x.MissionId.HasValue&&fixtureMissions.Contains(x.MissionId.Value)).ExecuteDeleteAsync();
            await db.PostFlightReports.Where(x=>fixtureMissions.Contains(x.MissionId)).ExecuteDeleteAsync();
            await db.FlightCompletionEvents.Where(x=>fixtureMissions.Contains(x.MissionId)).ExecuteDeleteAsync();
            await db.Incidents.Where(x=>fixtureMissions.Contains(x.MissionId)).ExecuteDeleteAsync();
            await db.Missions.Where(x=>fixtureMissions.Contains(x.Id)).ExecuteDeleteAsync();
            await db.UAVs.Where(x=>x.Id==uav).ExecuteDeleteAsync();
            await db.Pilots.Where(x=>x.Id==pilot).ExecuteDeleteAsync();
            await db.Users.Where(x=>new[]{owner,outsider}.Contains(x.Id)).ExecuteDeleteAsync();
            Console.WriteLine("Temporary lifecycle records removed.");
        }
    }
}
