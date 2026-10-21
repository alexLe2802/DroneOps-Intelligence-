using DroneOps.Application.Services.Missions;
using DroneOps.Domain.Entities;
using DroneOps.Persistence.Data;
using Microsoft.EntityFrameworkCore;

public sealed class MissionPreflightMonitor(IServiceScopeFactory scopes, ILogger<MissionPreflightMonitor> logger) : BackgroundService
{
    public static IQueryable<Mission> DueMissions(AppDbContext db, DateTime now)
    {
        var horizon = now.AddMinutes(15);
        var stale = new DateTimeOffset(DateTime.SpecifyKind(now, DateTimeKind.Utc)).AddMinutes(-1);
        return db.Missions.AsNoTracking()
            .Where(m => m.Status == "Approved" && m.StartTime <= horizon && m.EndTime > now
                && !db.MissionValidationRuns.Any(r => r.MissionId == m.Id && r.Stage == "Preflight" && r.CheckedAt > stale))
            .OrderBy(m => db.MissionValidationRuns.Where(r => r.MissionId == m.Id && r.Stage == "Preflight")
                .Max(r => (DateTimeOffset?)r.CheckedAt) ?? DateTimeOffset.MinValue)
            .ThenBy(m => m.StartTime);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        do
        {
            try
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var due = await DueMissions(db, DateTime.UtcNow)
                    .Select(m => new { m.Id, m.CreatedBy }).Take(100).ToListAsync(stoppingToken);
                await Parallel.ForEachAsync(due, new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = stoppingToken }, async (m, ct) =>
                {
                    try
                    {
                        using var missionScope = scopes.CreateScope();
                        await missionScope.ServiceProvider.GetRequiredService<MissionValidationService>()
                            .RevalidateAsync(m.Id, m.CreatedBy, true, "Preflight", ct);
                    }
                    catch (Exception e) when (!stoppingToken.IsCancellationRequested)
                    {
                        logger.LogError(e, "Preflight check failed for {MissionId}", m.Id);
                    }
                });
            }
            catch (Exception e) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(e, "Preflight monitor cycle failed");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
