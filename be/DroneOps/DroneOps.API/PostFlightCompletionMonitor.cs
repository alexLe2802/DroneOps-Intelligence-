using DroneOps.Application.Interfaces.PostFlight;
using DroneOps.Application.Settings;
using Microsoft.Extensions.Options;

/// <summary>
/// Theo dõi các mission đang bay và nối Flow 3 sang Flow 4.
/// Worker chỉ phát hiện ứng viên; toàn bộ kiểm tra an toàn vẫn nằm trong PostFlightService.
/// </summary>
public sealed class PostFlightCompletionMonitor : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptionsMonitor<PostFlightOptions> _options;
    private readonly ILogger<PostFlightCompletionMonitor> _logger;

    public PostFlightCompletionMonitor(
        IServiceScopeFactory scopeFactory,
        IOptionsMonitor<PostFlightOptions> options,
        ILogger<PostFlightCompletionMonitor> logger)
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
                await ProcessPostFlightReportsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                // Một chu kỳ lỗi không được làm background worker chết vĩnh viễn.
                _logger.LogError(exception, "Post-flight completion monitoring cycle failed.");
            }

            var delaySeconds = Math.Max(1, _options.CurrentValue.CompletionScanIntervalSeconds);
            await Task.Delay(TimeSpan.FromSeconds(delaySeconds), stoppingToken);
        }
    }

    private async Task ProcessPostFlightReportsAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IPostFlightService>();
        var synchronized = await service.SynchronizeOverdueMissionsAsync(cancellationToken);
        var processed = await service.ProcessQueuedReportsAsync(10, cancellationToken);
        if (synchronized > 0)
            _logger.LogInformation("Synchronized {MissionCount} overdue mission(s).", synchronized);
        if (processed > 0)
            _logger.LogInformation("Processed {ReportCount} queued post-flight report(s).", processed);
    }
}
