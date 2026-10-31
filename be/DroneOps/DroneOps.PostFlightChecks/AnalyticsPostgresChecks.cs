using System.Text.Json;
using DroneOps.Application.DTOs.Request.PostFlight;
using DroneOps.Application.Services.PostFlight;
using DroneOps.Application.Settings;
using DroneOps.Domain.Entities;
using DroneOps.Persistence;
using DroneOps.Persistence.Data;
using DroneOps.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

internal static class AnalyticsPostgresChecks
{
    public static async Task RunAsync(string root)
    {
        var settings=JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(root,"be/DroneOps/DroneOps.API/appsettings.Local.json")));
        var connection=settings.RootElement.GetProperty("ConnectionStrings").GetProperty("DefaultConnection").GetString()!;
        await using var source=PostgresDataSourceFactory.Create(connection);
        var options=new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(source).Options;
        await using var db=new AppDbContext(options);
        var service=new PostFlightService(new PostFlightRepository(db),
            Options.Create(new TelemetryMonitoringOptions{CommunicationLossSeconds=30}));
        var role=await db.Roles.Where(x=>x.Name=="uav_operator").Select(x=>x.Id).FirstAsync();
        var owner=Guid.NewGuid();var outsider=Guid.NewGuid();var pilotA=Guid.NewGuid();var pilotB=Guid.NewGuid();
        var uavA=Guid.NewGuid();var uavB=Guid.NewGuid();var outsideUav=Guid.NewGuid();
        var completed=Guid.NewGuid();var failed=Guid.NewGuid();var active=Guid.NewGuid();
        var cancelled=Guid.NewGuid();var outOfScope=Guid.NewGuid();var outsideWindow=Guid.NewGuid();
        var incidentA=Guid.NewGuid();var incidentB=Guid.NewGuid();
        var windowStart=DateTime.UtcNow.AddDays(-2).Date.AddHours(10);
        var from=windowStart.AddMinutes(-1);var to=windowStart.AddMinutes(1);
        int passed=0;
        void Check(bool condition,string label){if(!condition)throw new Exception("FAIL: "+label);Console.WriteLine("PASS: "+label);passed++;}
        Mission Flight(Guid id,Guid actor,Guid uav,string status,DateTime start)=>new(){Id=id,CreatedBy=actor,UAVId=uav,Name="Analytics "+id.ToString("N"),Status=status,StartTime=start,EndTime=status=="Completed"||status=="Failed"?start.AddMinutes(20):null,CreatedAt=start.AddHours(-1)};
        TelemetryRecord Point(Guid mission,Guid uav,int seconds,decimal? lat,decimal? lon,bool valid=true)=>new(){Id=Guid.NewGuid(),MissionId=mission,UAVId=uav,SourceId="analytics-fixture",SourceTimestamp=new DateTimeOffset(windowStart,TimeSpan.Zero).AddSeconds(seconds),ReceivedAt=new DateTimeOffset(windowStart,TimeSpan.Zero).AddSeconds(seconds),RecordedAt=new DateTimeOffset(windowStart,TimeSpan.Zero).AddSeconds(seconds),Latitude=lat,Longitude=lon,IsValid=valid};
        try
        {
            db.Users.AddRange(
                new User{Id=owner,RoleId=role,FullName="Analytics owner",Email=$"analytics-{owner:N}@test.invalid",PasswordHash="fixture",CreatedAt=DateTimeOffset.UtcNow},
                new User{Id=outsider,RoleId=role,FullName="Analytics outsider",Email=$"analytics-{outsider:N}@test.invalid",PasswordHash="fixture",CreatedAt=DateTimeOffset.UtcNow});
            db.Pilots.AddRange(new Pilot{Id=pilotA,UserId=owner,PilotLicenseNo="AN-A",CreatedAt=DateTime.UtcNow},new Pilot{Id=pilotB,UserId=outsider,PilotLicenseNo="AN-B",CreatedAt=DateTime.UtcNow});
            db.UAVs.AddRange(
                new UAV{Id=uavA,PilotId=pilotA,Code=$"AN-{uavA:N}",Name="Analytics A",Status="Active",CreatedAt=DateTimeOffset.UtcNow},
                new UAV{Id=uavB,PilotId=pilotA,Code=$"AN-{uavB:N}",Name="Analytics B",Status="Active",CreatedAt=DateTimeOffset.UtcNow},
                new UAV{Id=outsideUav,PilotId=pilotB,Code=$"AN-{outsideUav:N}",Name="Analytics outside",Status="Active",CreatedAt=DateTimeOffset.UtcNow});
            db.Missions.AddRange(
                Flight(completed,owner,uavA,"Completed",windowStart),Flight(failed,owner,uavA,"Failed",windowStart),
                Flight(active,owner,uavB,"In Progress",windowStart),Flight(cancelled,owner,uavA,"Cancelled",windowStart),
                Flight(outOfScope,outsider,outsideUav,"Completed",windowStart),Flight(outsideWindow,owner,uavA,"Completed",windowStart.AddDays(-5)));
            db.PostFlightReports.Add(new PostFlightReport{Id=Guid.NewGuid(),MissionId=completed,Revision=1,IsCurrent=true,GenerationStatus="Available",Status="Completed",TotalFlightDurationMinutes=20,CreatedAt=DateTimeOffset.UtcNow});
            db.TelemetryRecords.AddRange(
                Point(completed,uavA,0,10.0000m,106.0000m),Point(completed,uavA,10,10.0001m,106.0001m),
                Point(completed,uavA,100,10.0002m,106.0002m),Point(completed,uavA,110,null,null),
                Point(completed,uavA,120,10.0003m,106.0003m),Point(completed,uavA,125,10.00035m,106.00035m,false),Point(completed,uavA,130,10.0004m,106.0004m),
                Point(failed,uavA,0,null,null),
                Point(outOfScope,outsideUav,0,11m,107m),Point(outOfScope,outsideUav,10,11.001m,107.001m));
            db.Incidents.AddRange(
                new Incident{Id=incidentA,MissionId=completed,Title="A",Type="Safety",Severity="High",Status="Resolved",CreatedAt=DateTimeOffset.UtcNow},
                new Incident{Id=incidentB,MissionId=completed,Title="B",Type="Safety",Severity="Low",Status="Resolved",CreatedAt=DateTimeOffset.UtcNow},
                new Incident{Id=Guid.NewGuid(),MissionId=outOfScope,Title="Outside",Type="Safety",Severity="Critical",Status="Resolved",CreatedAt=DateTimeOffset.UtcNow});
            db.IncidentTransitions.AddRange(
                new IncidentTransition{Id=Guid.NewGuid(),IncidentId=incidentA,FromStatus="",ToStatus="Open",EventType="Raised",CreatedAt=DateTimeOffset.UtcNow},
                new IncidentTransition{Id=Guid.NewGuid(),IncidentId=incidentA,FromStatus="Open",ToStatus="Acknowledged",EventType="Acknowledged",CreatedAt=DateTimeOffset.UtcNow},
                new IncidentTransition{Id=Guid.NewGuid(),IncidentId=incidentA,FromStatus="Acknowledged",ToStatus="Resolved",EventType="Resolved",CreatedAt=DateTimeOffset.UtcNow});
            await db.SaveChangesAsync();db.ChangeTracker.Clear();

            var analytics=await service.GetOperationalAnalyticsAsync(new GetOperationalAnalyticsRequest{FromDate=from,ToDate=to},owner);
            Check(analytics.TotalFlights==3&&analytics.CompletedFlights==1&&analytics.SuccessRatePercentage==33.3,
                "completion is Completed / all role-scoped started flights in the start-time window");
            Check(analytics.TotalIncidentsRecorded==2&&analytics.IncidentSeverityBreakdown.Values.Sum()==2,
                "incident aggregate counts distinct incidents and never lifecycle transitions");
            Check(analytics.MissingCoverage.ValidDistanceSegments==1&&analytics.MissingCoverage.ExcludedDistanceSegments==1
                  && analytics.MissingCoverage.ExcludedGapSeconds==90&&analytics.MissingCoverage.MissingGpsSamples==2
                  && analytics.MissingCoverage.InvalidTelemetrySamples==1,
                "recorded distance uses consecutive valid GPS and reports missing/long-gap coverage");
            Check(analytics.TotalDistanceKilometers>0&&analytics.TotalDistanceKilometers<0.1m
                  && analytics.Contract.CommunicationLossThresholdSeconds==30,
                "recorded distance and communication-loss threshold use the documented units");
            Check(analytics.Contract.WindowBasis=="Mission.StartTime"&&analytics.Contract.WindowTimeZone=="UTC"
                  && analytics.Contract.Units["totalDistanceKilometers"]=="kilometers"
                  && analytics.Contract.SourceLinks.ContainsKey("missions"),
                "analytics contract exposes window, units and source links");

            var uavFiltered=await service.GetOperationalAnalyticsAsync(new GetOperationalAnalyticsRequest{FromDate=from,ToDate=to,UavId=uavA},owner);
            Check(uavFiltered.TotalFlights==2&&uavFiltered.CompletedFlights==1,"UAV filter is applied after role scope");
            var missionFiltered=await service.GetOperationalAnalyticsAsync(new GetOperationalAnalyticsRequest{MissionId=completed},owner);
            Check(missionFiltered.TotalFlights==1&&missionFiltered.CompletedFlights==1,
                "mission filter returns only the requested in-scope started flight");
            var forbiddenMission=await service.GetOperationalAnalyticsAsync(new GetOperationalAnalyticsRequest{MissionId=outOfScope},owner);
            Check(forbiddenMission.TotalFlights==0&&forbiddenMission.CompletionRateStatus=="Unavailable",
                "out-of-scope mission ID cannot influence an operator aggregate");
            var empty=await service.GetOperationalAnalyticsAsync(new GetOperationalAnalyticsRequest{MissionId=Guid.NewGuid()},owner);
            Check(empty.SuccessRatePercentage is null&&empty.CompletionRateStatus=="Unavailable",
                "empty denominator returns Unavailable rather than a misleading percentage");
            var manager=await service.GetOperationalAnalyticsAsync(new GetOperationalAnalyticsRequest{FromDate=from,ToDate=to},null);
            Check(manager.TotalFlights==4&&manager.TotalIncidentsRecorded==3,
                "manager aggregate includes all roles while operator aggregate remains owner-scoped");
            try{await service.GetOperationalAnalyticsAsync(new GetOperationalAnalyticsRequest{FromDate=to,ToDate=from},owner);throw new Exception("Invalid range accepted");}
            catch(ArgumentException){Check(true,"invalid date range is rejected");}
            Console.WriteLine($"{passed} PostgreSQL operational analytics checks passed.");
        }
        finally
        {
            db.ChangeTracker.Clear();
            // The production invariant makes transitions append-only. A fixture database
            // still needs deterministic cleanup, so hold an exclusive DDL lock and disable
            // only that trigger inside one rollback-safe transaction. Also remove remnants
            // from an interrupted prior analytics run by the reserved fixture email suffix.
            await using var cleanup=await db.Database.BeginTransactionAsync();
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"IncidentTransition\" DISABLE TRIGGER \"TR_IncidentTransition_AppendOnly\"");
            var fixtureOwners=db.Users.Where(x=>EF.Functions.Like(x.Email,"analytics-%@test.invalid")).Select(x=>x.Id);
            var fixtureMissions=db.Missions.Where(x=>fixtureOwners.Contains(x.CreatedBy)).Select(x=>x.Id);
            var fixtureIncidents=db.Incidents.Where(x=>fixtureMissions.Contains(x.MissionId)).Select(x=>x.Id);
            await db.IncidentAlertDeliveries.Where(x=>fixtureIncidents.Contains(x.IncidentId)).ExecuteDeleteAsync();
            await db.IncidentTransitions.Where(x=>fixtureIncidents.Contains(x.IncidentId)).ExecuteDeleteAsync();
            await db.Incidents.Where(x=>fixtureMissions.Contains(x.MissionId)).ExecuteDeleteAsync();
            await db.TelemetryRecords.Where(x=>x.MissionId.HasValue&&fixtureMissions.Contains(x.MissionId.Value)).ExecuteDeleteAsync();
            await db.PostFlightReports.Where(x=>fixtureMissions.Contains(x.MissionId)).ExecuteDeleteAsync();
            await db.FlightCompletionEvents.Where(x=>fixtureMissions.Contains(x.MissionId)).ExecuteDeleteAsync();
            await db.Missions.Where(x=>fixtureOwners.Contains(x.CreatedBy)).ExecuteDeleteAsync();
            var fixturePilots=db.Pilots.Where(x=>fixtureOwners.Contains(x.UserId)).Select(x=>x.Id);
            await db.UAVs.Where(x=>fixturePilots.Contains(x.PilotId)).ExecuteDeleteAsync();
            await db.Pilots.Where(x=>fixtureOwners.Contains(x.UserId)).ExecuteDeleteAsync();
            await db.Users.Where(x=>EF.Functions.Like(x.Email,"analytics-%@test.invalid")).ExecuteDeleteAsync();
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"IncidentTransition\" ENABLE TRIGGER \"TR_IncidentTransition_AppendOnly\"");
            await cleanup.CommitAsync();
            Console.WriteLine("Temporary analytics records removed.");
        }
    }
}
