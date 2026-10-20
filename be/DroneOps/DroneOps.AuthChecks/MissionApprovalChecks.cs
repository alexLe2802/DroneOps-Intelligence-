using DroneOps.Application.DTOs.Request.Missions;
using DroneOps.Application.Services.Missions;
using DroneOps.Domain.Entities;
using DroneOps.Persistence;
using DroneOps.Persistence.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

internal static class MissionApprovalChecks
{
    public static async Task RunAsync(string root)
    {
        var config = new ConfigurationBuilder().AddJsonFile(Path.Combine(root, "be/DroneOps/DroneOps.API/appsettings.Local.json")).Build();
        await using var source = PostgresDataSourceFactory.Create(config.GetConnectionString("DefaultConnection")!);
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(source, o => o.EnableRetryOnFailure()).Options;
        await using var db = new AppDbContext(options);
        var manager = await db.Users.AsNoTracking().Where(x => x.Role.Name == "operation_manager").Select(x => x.Id).FirstAsync();
        var creator = await db.Users.AsNoTracking().Where(x => x.Id != manager).Select(x => x.Id).FirstAsync();
        var uav = await db.UAVs.AsNoTracking().Select(x => x.Id).FirstAsync();
        var ids = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        var count = 0;
        void Check(bool value, string message) { if (!value) throw new Exception("FAIL: " + message); Console.WriteLine("PASS: " + message); count++; }
        try
        {
            foreach (var id in ids)
            {
                db.Missions.Add(new Mission {
                    Id = id, CreatedBy = creator, UAVId = uav, Name = "Temporary approval regression check", Status = "Pending",
                    CreatedAt = DateTime.UtcNow, StartTime = DateTime.UtcNow.AddDays(1), EndTime = DateTime.UtcNow.AddDays(1).AddHours(1),
                    Waypoints = [new Waypoint { Id = Guid.NewGuid(), MissionId = id, SequenceOrder = 1, Latitude = 10, Longitude = 106, Altitude = 50, CreatedAt = DateTime.UtcNow }]
                });
            }
            await db.SaveChangesAsync(); db.ChangeTracker.Clear();
            var service = new MissionApprovalService(db);
            var request = new ApproveMissionRequest { ExpectedStatus = "Pending", ExpectedVersion = 1, Comment = "Regression test" };
            var approved = await service.ApproveMissionAsync(ids[0], request, manager);
            Check(approved.NewStatus == "Approved", "approval works with Npgsql retry enabled");
            var replay = await service.ApproveMissionAsync(ids[0], request, manager);
            Check(replay.IdempotentReplay, "same decision replays despite original expected pending status");
            Check(await db.MissionApprovals.CountAsync(x => x.MissionId == ids[0]) == 1, "replayed approval does not duplicate audit");
            var rejected = await service.RejectMissionAsync(ids[1], new RejectMissionRequest { Reason = "Test findings", ExpectedStatus = "Pending" }, manager);
            Check(rejected.NewStatus == "Rejected", "rejection works with Npgsql retry enabled");
            Check(await db.MissionApprovals.CountAsync(x => x.MissionId == ids[1]) == 1, "rejection persists one audit record");
            try { await service.ApproveMissionAsync(ids[2], request, creator); throw new Exception("Self approval accepted"); }
            catch (InvalidOperationException e) when (e.Message.Contains("Self-approval")) { Check(true, "self approval remains forbidden"); }
            Check(await db.Missions.AsNoTracking().Where(x => x.Id == ids[2]).Select(x => x.Status).SingleAsync() == "Pending", "failed approval leaves status unchanged");
            Check(!await db.MissionApprovals.AnyAsync(x => x.MissionId == ids[2]), "failed approval leaves no audit");
            async Task<bool> DecideConcurrently()
            {
                await using var context = new AppDbContext(options);
                var result = await new MissionApprovalService(context).ApproveMissionAsync(ids[2], request, manager);
                return result.IdempotentReplay;
            }
            var results = await Task.WhenAll(DecideConcurrently(), DecideConcurrently());
            Check(results.Count(x => x) == 1, "concurrent identical decisions return one new decision and one replay");
            Check(await db.MissionApprovals.CountAsync(x => x.MissionId == ids[2]) == 1, "concurrent decisions do not duplicate audit");
            Console.WriteLine($"{count} PostgreSQL mission approval checks passed.");
        }
        finally
        {
            db.ChangeTracker.Clear();
            await db.MissionApprovals.Where(x => ids.Contains(x.MissionId)).ExecuteDeleteAsync();
            await db.Waypoints.Where(x => ids.Contains(x.MissionId)).ExecuteDeleteAsync();
            await db.Missions.Where(x => ids.Contains(x.Id)).ExecuteDeleteAsync();
            Console.WriteLine("Temporary missions, waypoints and decisions removed.");
        }
    }
}
