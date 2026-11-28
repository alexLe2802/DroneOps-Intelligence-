using System.Net;
using DroneOps.Application.DTOs.Options;
using DroneOps.Application.DTOs.Response.Missions;
using DroneOps.Application.Interfaces.Missions;
using DroneOps.Application.Services.Missions;
using DroneOps.Domain.Entities;
using DroneOps.Persistence.Interfaces;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
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

        await CheckReturnLegWeatherLabelingAsync();
    }

    private static async Task CheckReturnLegWeatherLabelingAsync()
    {
        using var http = new HttpClient(new UnavailableHandler());
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var weather = new MissionWeatherService(
            http,
            Options.Create(new MissionWeatherOptions { RouteSampleDistanceMeters = 1000 }),
            cache,
            NullLogger<MissionWeatherService>.Instance);

        var start = DateTime.UtcNow.AddHours(2);
        var mission = new Mission
        {
            Id = Guid.NewGuid(),
            StartTime = start,
            EndTime = start.AddMinutes(30),
            Waypoints =
            [
                new Waypoint { SequenceOrder = 1, Latitude = 10.80m, Longitude = 106.60m, Altitude = 30m, ActionType = "TakeOff" },
                new Waypoint { SequenceOrder = 2, Latitude = 10.81m, Longitude = 106.60m, Altitude = 30m, ActionType = "Waypoint" },
                new Waypoint { SequenceOrder = 3, Latitude = 10.82m, Longitude = 106.60m, Altitude = 30m, ActionType = "Return" },
            ]
        };

        var advisory = await weather.GetAsync(mission);
        var returnEndpoint = advisory.Waypoints.Last();
        var returnSamples = advisory.Waypoints.Where(w => w.IsReturnLeg && w.IsRouteSample).ToList();

        Check(returnEndpoint.IsReturnLeg && !returnEndpoint.IsRouteSample && returnEndpoint.SegmentFrom == 3 && returnEndpoint.SegmentTo == 1,
            "RTL projected endpoint is flagged as IsReturnLeg targeting WP1 instead of an extra waypoint");
        Check(returnSamples.Count > 0 && returnSamples.All(w => w.SegmentFrom == 3 && w.SegmentTo == 1),
            "route samples on the RTL return leg are flagged as IsReturnLeg from WP3 to WP1");
        Check(advisory.Warnings.Any(w => w.Contains("Return to WP1", StringComparison.Ordinal))
            && advisory.Warnings.Any(w => w.Contains("return leg WP3-WP1", StringComparison.Ordinal))
            && advisory.Warnings.All(w => !w.Contains("WP4", StringComparison.Ordinal)),
            "weather warnings label the RTL return leg clearly without inventing WP4");
    }

    private sealed class UnavailableHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
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
