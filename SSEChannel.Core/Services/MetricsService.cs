namespace SSEChannel.Core.Services;

/// <summary>
/// Service for exposing custom metrics for observability
/// </summary>
public class MetricsService : IMetricsService, IDisposable
{
    private readonly Meter _meter;
    private readonly Counter<long> _connectionsCounter;
    private readonly Counter<long> _messagesCounter;
    private readonly Counter<long> _groupOperationsCounter;
    private readonly Histogram<double> _messageLatency;
    private readonly ObservableGauge<int> _activeConnectionsGauge;
    private readonly ObservableGauge<int> _activeGroupsGauge;

    private readonly IScalableNotificationService _notificationService;
    private readonly IGroupNotificationService _groupService;

    public MetricsService(
        IScalableNotificationService notificationService,
        IGroupNotificationService groupService
    )
    {
        _notificationService = notificationService;
        _groupService = groupService;

        _meter = new Meter("SSEChannel.Core", "1.0.0");

        // Counters for tracking events
        _connectionsCounter = _meter.CreateCounter<long>(
            "ssechannel_connections_total",
            description: "Total number of SSE connections created"
        );

        _messagesCounter = _meter.CreateCounter<long>(
            "ssechannel_messages_total",
            description: "Total number of messages sent"
        );

        _groupOperationsCounter = _meter.CreateCounter<long>(
            "ssechannel_group_operations_total",
            description: "Total number of group operations performed"
        );

        // Histogram for latency tracking
        _messageLatency = _meter.CreateHistogram<double>(
            "ssechannel_message_latency_ms",
            unit: "ms",
            description: "Message delivery latency in milliseconds"
        );

        // Observable gauges for current state
        _activeConnectionsGauge = _meter.CreateObservableGauge<int>(
            "ssechannel_active_connections",
            description: "Current number of active SSE connections",
            observeValue: GetActiveConnections
        );

        _activeGroupsGauge = _meter.CreateObservableGauge<int>(
            "ssechannel_active_groups",
            description: "Current number of active groups",
            observeValue: GetActiveGroups
        );
    }

    /// <summary>
    /// Record a new connection
    /// </summary>
    public void RecordConnection(string connectionType = "sse")
    {
        _connectionsCounter.Add(1, new KeyValuePair<string, object?>("type", connectionType));
    }

    /// <summary>
    /// Record a message sent
    /// </summary>
    public void RecordMessage(string messageType = "broadcast", int recipientCount = 1)
    {
        _messagesCounter.Add(
            recipientCount,
            new KeyValuePair<string, object?>("type", messageType),
            new KeyValuePair<string, object?>("recipients", recipientCount)
        );
    }

    /// <summary>
    /// Record a group operation
    /// </summary>
    public void RecordGroupOperation(string operation, string? groupName = null)
    {
        _groupOperationsCounter.Add(
            1,
            new KeyValuePair<string, object?>("operation", operation),
            new KeyValuePair<string, object?>("group", groupName ?? "unknown")
        );
    }

    /// <summary>
    /// Record message delivery latency
    /// </summary>
    public void RecordMessageLatency(double latencyMs, string messageType = "broadcast")
    {
        _messageLatency.Record(latencyMs, new KeyValuePair<string, object?>("type", messageType));
    }

    private int GetActiveConnections()
    {
        try
        {
            var stats = _notificationService.GetStatsAsync().GetAwaiter().GetResult();
            return stats.ActiveConnections;
        }
        catch
        {
            return 0;
        }
    }

    private int GetActiveGroups()
    {
        try
        {
            var stats = _groupService.GetGroupStatisticsAsync().GetAwaiter().GetResult();
            return stats.Count;
        }
        catch
        {
            return 0;
        }
    }

    public void Dispose()
    {
        _meter?.Dispose();
    }
}
