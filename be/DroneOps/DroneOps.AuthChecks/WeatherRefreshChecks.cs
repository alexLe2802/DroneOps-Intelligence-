using DroneOps.Application.DTOs.Options;
using DroneOps.Application.DTOs.Response.Missions;
using DroneOps.Application.Interfaces.Missions;
using DroneOps.Application.Services.Missions;
using DroneOps.Domain.Entities;
using DroneOps.Persistence.Interfaces;
using Microsoft.Extensions.Options;

internal static class WeatherRefreshChecks
{
    public static async Task RunAsync()
    {
        var store = new Store();
        var provider = new Provider();
        var service = new MissionWeatherQueryService(store, provider, Options.Create(new MissionWeatherOptions()));
        Task<WeatherAdvisory> Read(bool refresh) =>
            service.ReadAsync(store.Mission.Id, store.Mission.CreatedBy, false, refresh, default);

        var burst = await Task.WhenAll(Enumerable.Range(0, 12).Select(i => Read(i % 2 == 0)));
        Check(provider.Calls == 1 && burst.All(w => w.Status == "Unavailable"), "parallel GET and refresh share one failed attempt");
        Check(store.Cache!.ExpiresAt > DateTimeOffset.UtcNow.AddSeconds(59), "unavailable retries wait 60 seconds");
        store.Cache.ExpiresAt = DateTimeOffset.UtcNow;
        provider.Status = "Available";
        var available = await Read(true);
        await Read(true);
        Check(provider.Calls == 2 && available.NextRefreshAt == available.ExpiresAt, "retry succeeds and fresh forecast is reused");

        store.Cache!.ExpiresAt = DateTimeOffset.UtcNow;
        provider.Status = "Unavailable";
        var stale = await Read(true);
        var repeated = await Read(false);
        Check(provider.Calls == 3 && repeated.Status == "Stale" && !repeated.Available &&
            stale.FetchedAt == available.FetchedAt && stale.ExpiresAt == available.ExpiresAt &&
            repeated.NextRefreshAt == stale.NextRefreshAt, "stale evidence preserves its timestamps during cooldown");
    }

    private static void Check(bool ok, string message)
    {
        if (!ok) throw new InvalidOperationException(message);
        Console.WriteLine("PASS: " + message);
    }

    private sealed class Provider : IMissionWeatherService
    {
        public int Calls;
        public string Status = "Unavailable";
        public async Task<WeatherAdvisory> GetAsync(Mission mission, CancellationToken ct = default)
        {
            Interlocked.Increment(ref Calls);
            await Task.Yield();
            return new WeatherAdvisory { Status = Status, Available = Status == "Available" };
        }
    }

    private sealed class Store : IMissionWeatherQueryRepository
    {
        private readonly SemaphoreSlim gate = new(1);
        public Mission Mission { get; } = new() { Id = Guid.NewGuid(), CreatedBy = Guid.NewGuid() };
        public MissionWeatherCache? Cache;
        public Task<Mission?> GetMissionWithWaypointsAsync(Guid id, CancellationToken ct = default) => Task.FromResult<Mission?>(Mission);
        public Task<MissionUpdateRequest?> GetReviewRequestAsync(Guid id, Guid requestId, CancellationToken ct) => throw new NotSupportedException();
        public Task<MissionWeatherCache?> GetCacheAsync(string key, CancellationToken ct = default) => Task.FromResult(Cache);
        public async Task<T> WithRefreshLockAsync<T>(string key, Func<Task<T>> action, CancellationToken ct)
        {
            await gate.WaitAsync(ct);
            try { return await action(); }
            finally { gate.Release(); }
        }
        public Task UpsertCacheAsync(string key, string json, DateTimeOffset fetched, DateTimeOffset expires, CancellationToken ct = default)
        {
            Cache = new MissionWeatherCache { Key = key, ForecastJson = json, FetchedAt = fetched, ExpiresAt = expires };
            return Task.CompletedTask;
        }
    }
}
