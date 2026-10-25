using DroneOps.Application.DTOs.Request.Telemetry;
using DroneOps.Application.Interfaces.Telemetry;
using DroneOps.Application.Services.Telemetry;
using DroneOps.Application.Services.Incidents;
using DroneOps.Application.Settings;
using DroneOps.Domain.Entities;
using DroneOps.Persistence;
using DroneOps.Persistence.Data;
using DroneOps.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

internal static class DroneConnectionChecks
{
    public static async Task RunAsync(string root)
    {
        var config=new ConfigurationBuilder().AddJsonFile(Path.Combine(root,"be/DroneOps/DroneOps.API/appsettings.Local.json")).Build();
        await using var source=PostgresDataSourceFactory.Create(config.GetConnectionString("DefaultConnection")!);
        var options=new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(source,o=>o.EnableRetryOnFailure()).Options;
        await using var db=new AppDbContext(options);
        await DroneConnectionSchema.CheckAsync(source);
        var role=await db.Roles.Where(r=>r.Name=="uav_operator").Select(r=>r.Id).FirstAsync();
        var owner=Guid.NewGuid();var pilot=Guid.NewGuid();var uav=Guid.NewGuid();var mission=Guid.NewGuid();var stranger=Guid.NewGuid();var otherUav=Guid.NewGuid();var otherMission=Guid.NewGuid();
        // Dùng dependency thật như runtime để kiểm tra constructor không bị lệch khi lifecycle incident thay đổi.
        var incidentLifecycle = new IncidentLifecycleService(db);
        var ingestion=new TelemetryIngestionService(
            db,
            NullLogger<TelemetryIngestionService>.Instance,
            Options.Create(new TelemetryMonitoringOptions()),
            incidentLifecycle);
        var service=new DroneConnectionService(new DroneConnectionRepository(db),ingestion);
        int count=0;
        void Check(bool ok,string label){if(!ok)throw new Exception("FAIL: "+label);Console.WriteLine("PASS: "+label);count++;}
        async Task Denied(Func<Task> action,string label){try{await action();throw new Exception("FAIL: "+label);}catch(UnauthorizedAccessException){Check(true,label);}}
        PairDroneGateway Pair(string code)=>new(code,"Temporary gateway fixture",1,1,"UID64-00000000075BCD15");
        try
        {
            db.Users.Add(new User{Id=owner,RoleId=role,FullName="Temporary gateway pilot",Email=$"gateway-{owner:N}@example.com",PasswordHash="disabled-fixture",CreatedAt=DateTimeOffset.UtcNow});
            db.Pilots.Add(new Pilot{Id=pilot,UserId=owner,PilotLicenseNo="FIXTURE",CreatedAt=DateTime.UtcNow});
            db.UAVs.Add(new UAV{Id=uav,PilotId=pilot,Name="Temporary gateway UAV",Code=$"GATEWAY-{uav:N}",Status="Active",CreatedAt=DateTimeOffset.UtcNow});
            db.Missions.Add(new Mission{Id=mission,UAVId=uav,CreatedBy=owner,Name="Temporary gateway mission",Status="Approved",StartTime=DateTime.UtcNow.AddDays(3),EndTime=DateTime.UtcNow.AddDays(3).AddHours(1),CreatedAt=DateTime.UtcNow});
            db.UAVs.Add(new UAV{Id=otherUav,PilotId=pilot,Name="Other registry entry",Code=$"GATEWAY-{otherUav:N}",Status="Active",CreatedAt=DateTimeOffset.UtcNow});
            db.Missions.Add(new Mission{Id=otherMission,UAVId=otherUav,CreatedBy=owner,Name="Other fixture mission",Status="Approved",StartTime=DateTime.UtcNow.AddDays(4),EndTime=DateTime.UtcNow.AddDays(4).AddHours(1),CreatedAt=DateTime.UtcNow});
            await db.SaveChangesAsync();db.ChangeTracker.Clear();
            await Denied(async()=>{await service.CreateAsync(stranger,new(uav,mission),default);},"another pilot cannot pair this UAV");
            var p=await service.CreateAsync(owner,new(uav,mission),default);
            var row=await db.DroneConnections.AsNoTracking().SingleAsync(c=>c.Id==p.Connection.Id);
            Check(row.PairCodeHash!=p.PairingCode&&row.TokenHash==null,"pairing secret is hashed; token does not exist before claim");
            Check(p.Connection.Status=="AwaitingGateway","creation waits for gateway, not a fake online drone");
            Check((await service.ListAsync(stranger,default)).Count==0,"session list is owner scoped");
            var credentials=await service.PairAsync(Pair(p.PairingCode),default);
            Check(credentials.UavId==uav&&credentials.MissionId==mission,"credential binds the selected UAV and mission");
            await Denied(async()=>{await service.PairAsync(Pair(p.PairingCode),default);},"pairing code cannot be replayed");
            row=await db.DroneConnections.AsNoTracking().SingleAsync(c=>c.Id==credentials.ConnectionId);
            Check(row.TokenHash!=credentials.Token&&row.TokenHash==DroneConnectionService.Hash(credentials.Token),"only token hash is stored");
            await Denied(()=>service.HeartbeatAsync(row.Id,new string('A',64),credentials.HardwareIdentity,default),"invalid token cannot send gateway heartbeat");
            await service.HeartbeatAsync(row.Id,credentials.Token,credentials.HardwareIdentity,default);
            Check((await service.ListAsync(owner,default))[0].Status=="GatewayConnected","gateway heartbeat alone does not claim drone telemetry");
            var sample=new IngestTelemetrySampleRequest{SourceId=credentials.SourceId,UavId=Guid.NewGuid(),MissionId=mission,SourceTimestamp=DateTimeOffset.UtcNow,Latitude=11.35m,Longitude=106.34m,AltitudeMeters=50,BatteryPercent=70,MissionSequence=1};
            await Denied(async()=>{await service.SampleAsync(row.Id,credentials.Token,credentials.HardwareIdentity,"wrong-map",sample,default);},"credential cannot inject another UAV");
            sample.UavId=uav;
            var accepted=await service.SampleAsync(row.Id,credentials.Token,credentials.HardwareIdentity,"same-sample",sample,default);
            var replay=await service.SampleAsync(row.Id,credentials.Token,credentials.HardwareIdentity,"same-sample",sample,default);
            Check(accepted.Accepted&&replay.IsDuplicate,"sample retries are idempotent through real ingestion");
            var latest=await ingestion.GetLatestMissionTelemetryAsync(mission,owner,false);
            Check(latest?.Latitude==11.35m&&latest.Longitude==106.34m,"paired telemetry reaches existing latest projection");
            Check((await service.ListAsync(owner,default))[0].Status=="ReceivingTelemetry","web status distinguishes received drone samples");
            Check(!await service.RevokeAsync(row.Id,stranger,default),"another pilot cannot revoke this session");
            await service.RevokeAsync(row.Id,owner,default);
            await Denied(()=>service.HeartbeatAsync(row.Id,credentials.Token,credentials.HardwareIdentity,default),"revocation immediately rejects the old token");
            if(Environment.GetEnvironmentVariable("DRONEOPS_GATEWAY_PYTHON") is not null)
                await DroneGatewayHttpChecks.RunAsync(root,owner,uav,mission);
            Check((await service.BindingsAsync(owner,default)).Single().HardwareIdentity==credentials.HardwareIdentity,"hardware binding survives session revocation and subsequent pairing");
            Check((await service.BindingsAsync(stranger,default)).Count==0,"hardware bindings are owner scoped");
            var wrong=await service.CreateAsync(owner,new(uav,mission),default);
            try {await service.PairAsync(Pair(wrong.PairingCode) with {HardwareIdentity="UID64-0000000000000002"},default);throw new Exception("Wrong physical drone accepted");}
            catch(InvalidOperationException){Check(true,"registered UAV cannot be silently rebound to another physical drone");}
            var reconnected=await service.PairAsync(Pair(wrong.PairingCode),default);
            Check(reconnected.HardwareIdentity==credentials.HardwareIdentity,"same physical drone reconnects after a new session");
            await Denied(()=>service.HeartbeatAsync(wrong.Connection.Id,reconnected.Token,"UID64-0000000000000002",default),"saved credential rejects a different observed drone identity");
            var duplicateRegistry=await service.CreateAsync(owner,new(otherUav,otherMission),default);
            try {await service.PairAsync(Pair(duplicateRegistry.PairingCode),default);throw new Exception("Duplicate physical drone accepted");}
            catch(InvalidOperationException){Check(true,"one physical drone cannot be assigned to a second registry entry");}
            try {await service.PairAsync(Pair(duplicateRegistry.PairingCode) with {HardwareIdentity=""},default);throw new Exception("Missing hardware UID accepted");}
            catch(ArgumentException){Check(true,"system ID and pairing code alone cannot substitute for hardware identity");}
            var expired=await service.CreateAsync(owner,new(uav,mission),default);
            await db.DroneConnections.Where(c=>c.Id==expired.Connection.Id).ExecuteUpdateAsync(s=>s.SetProperty(c=>c.CodeExpiresAt,DateTimeOffset.UtcNow.AddMinutes(-1)));
            await Denied(async()=>{await service.PairAsync(Pair(expired.PairingCode),default);},"expired code cannot be redeemed");
            var first=await service.CreateAsync(owner,new(uav,mission),default);
            var firstCredential=await service.PairAsync(Pair(first.PairingCode),default);
            var second=await service.CreateAsync(owner,new(uav,mission),default);
            await Denied(()=>service.HeartbeatAsync(first.Connection.Id,firstCredential.Token,firstCredential.HardwareIdentity,default),"new pairing revokes the previous UAV session");
            var simultaneous=await Task.WhenAll(Enumerable.Range(0,2).Select(async _=>
            {
                await using var ctx=new AppDbContext(options);
                var svc=new DroneConnectionService(new DroneConnectionRepository(ctx),ingestion);
                try{return await svc.PairAsync(Pair(second.PairingCode),default);}catch(UnauthorizedAccessException){return null;}
            }));
            Check(simultaneous.Count(x=>x is not null)==1,"concurrent claims consume the code exactly once");
            var activeCredential=simultaneous.Single(x=>x is not null)!;
            // A moved/deactivated aircraft must invalidate credentials even before TTL.
            await db.UAVs.Where(u=>u.Id==uav).ExecuteUpdateAsync(s=>s.SetProperty(u=>u.Status,"Inactive"));
            await Denied(()=>service.HeartbeatAsync(second.Connection.Id,activeCredential.Token,activeCredential.HardwareIdentity,default),"deactivating aircraft invalidates an already paired credential");
            var uavRepository = new UavRepository(db);
            Check(!await uavRepository.DeleteOwnedAsync(uav,stranger,false),"another pilot cannot delete an owned UAV");
            Check(await uavRepository.DeleteOwnedAsync(uav,owner,false),"pilot can delete their own UAV regardless of status and hardware binding");
            Check(!await db.UAVs.AsNoTracking().AnyAsync(x=>x.Id==uav)
                  && !await db.DroneConnections.AsNoTracking().AnyAsync(x=>x.UavId==uav)
                  && !await db.DroneHardwareIdentities.AsNoTracking().AnyAsync(x=>x.UavId==uav)
                  && !await db.TelemetryRecords.AsNoTracking().AnyAsync(x=>x.UAVId==uav)
                  && await db.Missions.AsNoTracking().Where(x=>x.Id==mission).Select(x=>x.UAVId).SingleAsync()==null,
                "UAV deletion clears required dependents and preserves the mission without an aircraft assignment");
            Console.WriteLine($"{count} PostgreSQL gateway checks passed. Hardware is not used.");
        }
        finally
        {
            db.ChangeTracker.Clear();
            await db.DroneConnections.Where(c=>c.OwnerId==owner).ExecuteDeleteAsync();
            await db.DroneHardwareIdentities.Where(h=>h.BoundBy==owner).ExecuteDeleteAsync();
            await db.Incidents.Where(c=>c.MissionId==mission).ExecuteDeleteAsync();
            await db.TelemetryRecords.Where(c=>c.MissionId==mission).ExecuteDeleteAsync();
            await db.Missions.Where(c=>c.CreatedBy==owner).ExecuteDeleteAsync();
            await db.UAVs.Where(c=>c.PilotId==pilot).ExecuteDeleteAsync();
            await db.Pilots.Where(c=>c.Id==pilot).ExecuteDeleteAsync();
            await db.Users.Where(c=>c.Id==owner).ExecuteDeleteAsync();
            Console.WriteLine("Temporary gateway records removed.");
        }
    }
}
