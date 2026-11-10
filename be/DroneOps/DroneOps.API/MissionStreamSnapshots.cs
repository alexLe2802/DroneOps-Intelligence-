using System.Diagnostics;
using DroneOps.Application.DTOs.Response.Incidents;
using DroneOps.Application.DTOs.Response.Telemetry;
using DroneOps.Application.Interfaces.Incidents;
using DroneOps.Application.Interfaces.Telemetry;

namespace DroneOps.API;

// Only authorized controllers may subscribe. Entries live exactly as long as their subscribers.
// Each refresh owns its DI scope: a DbContext is never shared by concurrent requests.
public sealed class MissionStreamSnapshots(IServiceScopeFactory scopes)
{
    private readonly IServiceScopeFactory scopeFactory = scopes;
    private readonly object sync = new();
    private readonly Dictionary<Guid, Entry> entries = new();
    public sealed record Snapshot(MissionLatestTelemetryResponse? Telemetry, IReadOnlyList<IncidentItemResponse> Incidents);
    internal sealed class Entry
    {
        public int Subscribers;
        public readonly SemaphoreSlim Gate = new(1, 1);
        public Snapshot? Value;
        public long RefreshedAt;
        public DateTimeOffset? Since;
        public readonly Dictionary<Guid, IncidentItemResponse> Incidents = new();
    }

    public Subscription Subscribe(Guid missionId)
    {
        lock (sync)
        {
            if (!entries.TryGetValue(missionId, out var entry)) entries[missionId] = entry = new();
            entry.Subscribers++;
            return new(this, missionId, entry);
        }
    }

    public sealed class Subscription : IDisposable
    {
        private readonly MissionStreamSnapshots owner;
        private readonly Guid missionId;
        private readonly Entry entry;
        private int disposed;
        internal Subscription(MissionStreamSnapshots owner, Guid missionId, Entry entry)
        { this.owner = owner; this.missionId = missionId; this.entry = entry; }

        public async Task<Snapshot> ReadAsync(CancellationToken ct)
        {
            ObjectDisposedException.ThrowIf(disposed != 0, this);
            await entry.Gate.WaitAsync(ct);
            try
            {
                if (entry.Value != null && Stopwatch.GetElapsedTime(entry.RefreshedAt) < TimeSpan.FromMilliseconds(1500))
                    return entry.Value;
                await using var scope = owner.scopeFactory.CreateAsyncScope();
                // Authorization was performed for this subscriber before acquiring the subscription.
                var telemetry = await scope.ServiceProvider.GetRequiredService<ITelemetryIngestionService>()
                    .GetLatestMissionTelemetryAsync(missionId, null, true, ct);
                var refreshStarted = DateTimeOffset.UtcNow;
                var changes = await scope.ServiceProvider.GetRequiredService<IIncidentService>()
                    .GetMissionIncidentFeedAsync(missionId, null, true, entry.Since, ct);
                foreach (var incident in changes) entry.Incidents[incident.Id] = incident;
                // Overlap handles equal timestamps and transactions becoming visible between polls.
                entry.Since = refreshStarted.AddSeconds(-2);
                entry.Value = new(telemetry, entry.Incidents.Values.ToArray());
                entry.RefreshedAt = Stopwatch.GetTimestamp();
                return entry.Value;
            }
            finally { entry.Gate.Release(); }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0) return;
            lock (owner.sync)
            {
                if (--entry.Subscribers == 0) owner.entries.Remove(missionId);
            }
        }
    }
}
