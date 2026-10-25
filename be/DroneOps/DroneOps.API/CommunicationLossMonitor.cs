using System.Text.Json;
using DroneOps.Application.Settings;
using DroneOps.Domain.Entities;
using DroneOps.Persistence.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using DroneOps.Application.Interfaces.Incidents;
using DroneOps.Application.Interfaces.Notifications;

public sealed class CommunicationLossMonitor : BackgroundService
{
    private static readonly string[] ActiveMissionStatuses =
        ["Approved", "In Progress", "InProgress", "Active", "Flying", "Executing"];

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptionsMonitor<TelemetryMonitoringOptions> _options;
    private readonly ILogger<CommunicationLossMonitor> _logger;

    public CommunicationLossMonitor(
        IServiceScopeFactory scopeFactory,
        IOptionsMonitor<TelemetryMonitoringOptions> options,
        ILogger<CommunicationLossMonitor> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await DetectCommunicationLossAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Communication-loss monitoring cycle failed.");
            }

            var delay = Math.Max(1, _options.CurrentValue.ScanIntervalSeconds);
            await Task.Delay(TimeSpan.FromSeconds(delay), stoppingToken);
        }
    }

    private async Task DetectCommunicationLossAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var lifecycle = scope.ServiceProvider.GetRequiredService<IIncidentLifecycleService>();
        var notificationService = scope.ServiceProvider.GetRequiredService<INotificationService>();
        var options = _options.CurrentValue;
        var cutoff = DateTimeOffset.UtcNow.AddSeconds(-Math.Max(1, options.CommunicationLossSeconds));

        var candidates = await context.Missions
            .Where(m => ActiveMissionStatuses.Contains(m.Status))
            .Select(m => new
            {
                Mission = m,
                Latest = context.TelemetryRecords
                    .Where(t => t.MissionId == m.Id && t.IsValid)
                    .OrderByDescending(t => t.ReceivedAt)
                    .FirstOrDefault()
            })
            .Where(x => x.Latest != null && x.Latest.ReceivedAt < cutoff)
            .ToListAsync(cancellationToken);

        var raisedIncidents = new List<(Guid IncidentId, Guid PilotUserId)>();
        foreach (var candidate in candidates)
        {
            var hasOpenIncident = await context.Incidents.AnyAsync(
                i => i.MissionId == candidate.Mission.Id &&
                     i.Type == "CommunicationLoss" &&
                     i.Status != "Resolved",
                cancellationToken);

            if (hasOpenIncident)
            {
                continue;
            }

            var latest = candidate.Latest!;
            var raisedIncidentId = await lifecycle.RaiseIfOpenMissingAsync(new Incident
            {
                Id = Guid.NewGuid(),
                MissionId = candidate.Mission.Id,
                Title = "Telemetry communication lost",
                Description = $"No telemetry packet received for more than {options.CommunicationLossSeconds} seconds.",
                Type = "CommunicationLoss",
                Severity = "Critical",
                Status = "Open",
                Latitude = latest.Latitude,
                Longitude = latest.Longitude,
                Evidence = JsonSerializer.Serialize(new
                {
                    sourceId = latest.SourceId,
                    latitude = latest.Latitude,
                    longitude = latest.Longitude,
                    altitude = latest.Altitude,
                    speed = latest.Speed,
                    batteryPercent = latest.BatteryLevel,
                    sourceTimestamp = latest.SourceTimestamp,
                    receivedAt = latest.ReceivedAt,
                    detectedAt = DateTimeOffset.UtcNow,
                    appliedRule = "CommunicationLoss",
                    threshold = options.CommunicationLossSeconds
                }),
                CreatedAt = DateTimeOffset.UtcNow
            }, cancellationToken);
            if (raisedIncidentId.HasValue)
            {
                raisedIncidents.Add((raisedIncidentId.Value, candidate.Mission.CreatedBy));
            }
        }

        if (context.ChangeTracker.HasChanges())
        {
            await context.SaveChangesAsync(cancellationToken);
        }

        foreach (var raised in raisedIncidents)
        {
            try
            {
                await notificationService.PublishIncidentRaisedAsync(
                    raised.IncidentId, "CommunicationLoss", raised.PilotUserId, cancellationToken);
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning(exception,
                    "Could not publish communication-loss notification for {IncidentId}.", raised.IncidentId);
            }
        }
    }
}
