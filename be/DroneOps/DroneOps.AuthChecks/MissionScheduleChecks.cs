using DroneOps.Application.Validators.Missions;
using DroneOps.Domain.Entities;
using DroneOps.Persistence.Data;
using DroneOps.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

internal static class MissionScheduleChecks
{
    public static async Task RunAsync()
    {
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var repository = new MissionValidationRepository(db, cache);
        var owner = Guid.NewGuid(); var other = Guid.NewGuid();
        var uav = Guid.NewGuid(); var otherUav = Guid.NewGuid(); var candidate = Guid.NewGuid();
        var start = new DateTime(2026, 11, 5, 4, 0, 0, DateTimeKind.Utc);
        var end = start.AddHours(2);
        var count = 0;
        void Check(bool ok, string text) { if (!ok) throw new InvalidOperationException(text); count++; Console.WriteLine("PASS: " + text); }
        async Task<List<DroneOps.Persistence.Interfaces.MissionScheduleConflict>> Find(Guid? aircraft = null) =>
            await repository.GetScheduleConflictsAsync(candidate, aircraft ?? uav, owner, start, end);
        async Task Set(string status, Guid pilot, Guid? aircraft, DateTime? from = null, DateTime? until = null, Guid? id = null)
        {
            db.Missions.RemoveRange(db.Missions); await db.SaveChangesAsync();
            db.Missions.Add(new Mission { Id = id ?? Guid.NewGuid(), Name = "Bay thử", CreatedBy = pilot,
                UAVId = aircraft, Status = status, StartTime = from ?? start, EndTime = until ?? end });
            await db.SaveChangesAsync();
        }
        await Set("Approved", owner, otherUav);
        var conflicts = await Find();
        Check(conflicts is [{ OperatorConflict: true, UavConflict: false }], "another UAV still conflicts when the same operator is booked");
        var text = MissionValidationService.DescribeScheduleConflict(conflicts.Single());
        Check(text.StartsWith("Your operator schedule") && text.Contains("Bay thử") && text.Contains("05/11/2026 11:00")
            && text.Contains("GMT+7") && text.Contains("changing UAV alone"), "operator conflict names the booking, Vietnam time and correct remedy");
        await Set("Approved", other, uav);
        conflicts = await Find();
        text = MissionValidationService.DescribeScheduleConflict(conflicts.Single());
        Check(conflicts is [{ OperatorConflict: false, UavConflict: true }] && text.StartsWith("The selected UAV")
            && !text.Contains("changing UAV alone"), "aircraft-only conflict does not falsely claim the operator is busy");
        await Set("InProgress", owner, uav);
        Check((await Find()) is [{ OperatorConflict: true, UavConflict: true }], "both resource conflicts are retained");
        await Set("In Progress", other, uav);
        Check((await Find()).Count == 1, "legacy active mission status still reserves the aircraft");
        await Set("Approved", other, otherUav);
        Check((await Find()).Count == 0, "unrelated aircraft and operator never block this booking");
        await Set("Approved", owner, uav, id: candidate);
        Check((await Find()).Count == 0, "editing excludes the mission being validated");
        await Set("Approved", owner, uav, start.AddHours(-2), start);
        Check((await Find()).Count == 0, "previous mission ending exactly at start does not overlap");
        await Set("Approved", owner, uav, end, end.AddHours(1));
        Check((await Find()).Count == 0, "next mission starting exactly at end does not overlap");
        foreach (var status in new[] { "Completed", "Cancelled", "Rejected", "Pending", "Pending Confirm", "Draft" })
        {
            await Set(status, owner, uav);
            Check((await Find()).Count == 0, status + " missions do not reserve the schedule");
        }
        await Set("Approved", other, null);
        Check((await repository.GetScheduleConflictsAsync(candidate, null, owner, start, end)).Count == 0,
            "two unassigned aircraft are not a shared aircraft conflict");
        Check((await repository.GetScheduleConflictsAsync(candidate, uav, owner, null, end)).Count == 0,
            "missing candidate schedule does not fabricate a conflict");
        Console.WriteLine($"Mission schedule checks passed: {count}");
    }
}
