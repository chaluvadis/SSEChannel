namespace SSEChannel.Core.Producers;

/// <summary>
/// High-performance background service for generating notifications to millions of clients
/// </summary>
public class ScalableNotificationProducer : BackgroundService
{
    private readonly IScalableNotificationService _notificationService;
    private readonly IGroupNotificationService _groupService;
    private readonly ILogger<ScalableNotificationProducer> _logger;
    private readonly IConfiguration _configuration;

    // Configuration settings
    private readonly int _broadcastIntervalMs;
    private readonly int _statsIntervalMs;
    private readonly bool _enableAutoBroadcast;
    private readonly bool _enableStatsLogging;

    public ScalableNotificationProducer(
        IScalableNotificationService notificationService,
        IGroupNotificationService groupService,
        ILogger<ScalableNotificationProducer> logger,
        IConfiguration configuration
    )
    {
        _notificationService = notificationService;
        _groupService = groupService;
        _logger = logger;
        _configuration = configuration;

        // Load configuration
        _broadcastIntervalMs = _configuration.GetValue("Notifications:BroadcastIntervalMs", 2000);
        _statsIntervalMs = _configuration.GetValue("Notifications:StatsIntervalMs", 10000);
        _enableAutoBroadcast = _configuration.GetValue("Notifications:EnableAutoBroadcast", true);
        _enableStatsLogging = _configuration.GetValue("Notifications:EnableStatsLogging", true);

        _logger.LogInformation(
            "ScalableNotificationProducer initialized - Broadcast: {BroadcastInterval}ms, Stats: {StatsInterval}ms, AutoBroadcast: {AutoBroadcast}",
            _broadcastIntervalMs,
            _statsIntervalMs,
            _enableAutoBroadcast
        );
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("ScalableNotificationProducer started");

        // Create separate tasks for different operations
        var tasks = new List<Task>();

        if (_enableAutoBroadcast)
        {
            tasks.Add(BroadcastLoop(stoppingToken));
        }

        if (_enableStatsLogging)
        {
            tasks.Add(StatsLoop(stoppingToken));
        }

        // Admin notifications are now handled through GroupNotificationRoutes

        try
        {
            // Wait for any task to complete (shouldn't happen unless cancelled)
            await Task.WhenAny(tasks);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("ScalableNotificationProducer stopping...");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ScalableNotificationProducer encountered an error");
        }
        finally
        {
            _logger.LogInformation("ScalableNotificationProducer stopped");
        }
    }

    /// <summary>
    /// Main broadcast loop - sends periodic messages to all connected clients
    /// </summary>
    private async Task BroadcastLoop(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(_broadcastIntervalMs));
        int counter = 1;

        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug(
                "Broadcast loop started with {Interval}ms interval",
                _broadcastIntervalMs
            );
        }

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                var timestamp = DateTime.UtcNow.ToString("HH:mm:ss.fff");
                var message = $"Global broadcast #{counter:D6} at {timestamp}";

                // High-performance broadcast to all clients
                await _notificationService.PublishToAllAsync(message);

                if (counter % 10 == 0 && _logger.IsEnabled(LogLevel.Debug)) // Log every 10th broadcast to avoid spam
                {
                    _logger.LogDebug("Sent broadcast #{Counter}", counter);
                }

                counter++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during broadcast #{Counter}", counter);
            }
        }
    }

    /// <summary>
    /// Statistics logging loop - periodically logs system performance
    /// </summary>
    private async Task StatsLoop(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(_statsIntervalMs));

        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug("Stats loop started with {Interval}ms interval", _statsIntervalMs);
        }

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                var stats = await _notificationService.GetStatsAsync();
                var groupStats = await _groupService.GetGroupStatisticsAsync();

                _logger.LogInformation(
                    "Performance Stats - Active: {ActiveConnections}, Total: {TotalConnections}, "
                        + "Groups: {GroupCount}, Messages/sec: {MessagesPerSecond}",
                    stats.ActiveConnections,
                    stats.TotalConnections,
                    groupStats.Count,
                    stats.MessagesPerSecond
                );

                // Admin stats notifications are now handled through GroupNotificationRoutes
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during stats collection");
            }
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("ScalableNotificationProducer stopping...");

        // Admin shutdown notifications are now handled through GroupNotificationRoutes

        await base.StopAsync(cancellationToken);
        _logger.LogInformation("ScalableNotificationProducer stopped successfully");
    }
}
