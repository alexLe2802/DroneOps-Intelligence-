using System.Diagnostics;
using DroneOps.Application.DTOs.Request.PostFlight;
using DroneOps.Application.Services;
using DroneOps.Application.Services.PostFlight;
using DroneOps.Application.Validators.Missions;
using DroneOps.Persistence;
using DroneOps.Persistence.Data;
using DroneOps.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

internal static class ReadPerformanceChecks
{
    public static async Task RunAsync(string root)
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(root, "be/DroneOps/DroneOps.API/appsettings.Local.json"))
            .Build();
        await using var source = PostgresDataSourceFactory.Create(configuration.GetConnectionString("DefaultConnection")!);
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(source).Options);
        using var cache = new MemoryCache(new MemoryCacheOptions());
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var ct = deadline.Token;
        var owner = await db.Missions.AsNoTracking().GroupBy(m => m.CreatedBy)
            .OrderByDescending(group => group.Count()).Select(group => group.Key).FirstAsync(ct);
        var postFlight = new PostFlightService(new PostFlightRepository(db));
        var validation = new MissionValidationService(new MissionValidationRepository(db, cache), null!);
        var missions = new MissionService(new MissionRepository(db), null!, validation,
            postFlight, null!, null!, NullLogger<MissionService>.Instance);
        for (var iteration = 1; iteration <= 2; iteration++)
        {
            var watch = Stopwatch.StartNew();
            var mine = await missions.GetMyAsync(owner, ct);
            Console.WriteLine($"My missions pass {iteration}: {watch.ElapsedMilliseconds} ms, {mine.Count} missions");
            watch.Restart();
            var history = await postFlight.GetFlightHistoryAsync(new GetFlightHistoryRequest { PageSize = 100 }, owner, ct);
            Console.WriteLine($"Dashboard history pass {iteration}: {watch.ElapsedMilliseconds} ms, {history.Items.Count} missions");
            watch.Restart();
            var analytics = await postFlight.GetOperationalAnalyticsAsync(new GetOperationalAnalyticsRequest(), owner, ct);
            Console.WriteLine($"Dashboard analytics pass {iteration}: {watch.ElapsedMilliseconds} ms, {analytics.TotalFlights} flights");
        }
    }
}
