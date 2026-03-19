namespace SSEChannel.Core.Services;

/// <summary>
/// High-performance notification service designed to handle millions of concurrent connections
/// Uses connection partitioning and parallel processing for optimal scalability
/// </summary>
public class ScalableNotificationService : IScalableNotificationService, IDisposable
{
    private readonly int _partitionCount;
    private readonly ConnectionPartitionService[] _partitions;
    private readonly ConcurrentDictionary<string, int> _connectionToPartition = new();
    private readonly Timer _cleanupTimer;
    private readonly ILogger<ScalableNotificationService> _logger;
    private readonly IConnectionEventService _connectionEventService;

    // Performance tracking
    private long _messageCounter;
    private DateTime _lastStatsUpdate = DateTime.UtcNow;

    public ScalableNotificationService(
        IConfiguration configuration,
        ILogger<ScalableNotificationService> logger,
        ILoggerFactory loggerFactory,
        IConnectionEventService connectionEventService
    )
    {
        _logger = logger;
        _connectionEventService = connectionEventService;

        // Configure partitioning based on CPU cores for optimal performance
        _partitionCount = configuration.GetValue(
            "Notifications:PartitionCount",
            Environment.ProcessorCount * 2
        );

        _partitions = new ConnectionPartitionService[_partitionCount];

        // Initialize partitions with individual loggers
        for (int i = 0; i < _partitionCount; i++)
        {
            var partitionLogger = loggerFactory.CreateLogger<ConnectionPartitionService>();
            _partitions[i] = new ConnectionPartitionService(i, partitionLogger);
        }

        // Setup automatic cleanup of disconnected clients
        var cleanupInterval = configuration.GetValue("Notifications:CleanupIntervalMs", 30000);
        _cleanupTimer = new Timer(
            CleanupDisconnectedClients,
            null,
            TimeSpan.FromMilliseconds(cleanupInterval),
            TimeSpan.FromMilliseconds(cleanupInterval)
        );

        _logger.LogInformation(
            "ScalableNotificationService initialized with {PartitionCount} partitions, cleanup interval: {CleanupInterval}ms",
            _partitionCount,
            cleanupInterval
        );
    }

    /// <summary>
    /// Connect a new client and assign to a partition using consistent hashing
    /// </summary>
    public ValueTask<string> ConnectAsync(string? connectionId = null)
    {
        connectionId ??= Guid.NewGuid().ToString("N");

        // Use consistent hashing to distribute connections evenly across partitions
        var partitionIndex = Math.Abs(connectionId.GetHashCode()) % _partitionCount;
        var partition = _partitions[partitionIndex];

        partition.AddConnection(connectionId);
        _connectionToPartition[connectionId] = partitionIndex;

        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug(
                "Connected client {ConnectionId} to partition {PartitionIndex}",
                connectionId,
                partitionIndex
            );
        }

        return ValueTask.FromResult(connectionId);
    }

    /// <summary>
    /// Disconnect a client and clean up resources
    /// </summary>
    public async ValueTask DisconnectAsync(string connectionId)
    {
        if (_connectionToPartition.TryRemove(connectionId, out var partitionIndex))
        {
            _partitions[partitionIndex].RemoveConnection(connectionId);

            // Notify connection event service about disconnection
            await _connectionEventService.OnConnectionDisconnectedAsync(connectionId);

            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug(
                    "Disconnected client {ConnectionId} from partition {PartitionIndex}",
                    connectionId,
                    partitionIndex
                );
            }
        }
    }

    /// <summary>
    /// Send message to a specific connection
    /// </summary>
    public ValueTask PublishToConnectionAsync(string connectionId, string message)
    {
        if (_connectionToPartition.TryGetValue(connectionId, out var partitionIndex))
        {
            return _partitions[partitionIndex].PublishToConnectionAsync(connectionId, message);
        }

        _logger.LogWarning(
            "Attempted to publish to non-existent connection {ConnectionId}",
            connectionId
        );
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Send message to all connected clients across all partitions
    /// </summary>
    public async ValueTask PublishToAllAsync(string message)
    {
        // Parallel publish across all partitions for maximum throughput
        var tasks = _partitions
            .Select(partition => partition.PublishToAllAsync(message).AsTask())
            .ToArray();

        await Task.WhenAll(tasks);

        var totalConnections = _partitions.Sum(p => p.ConnectionCount);
        Interlocked.Add(ref _messageCounter, totalConnections);

        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug(
                "Published message to all {ConnectionCount} connections across {PartitionCount} partitions",
                totalConnections,
                _partitionCount
            );
        }
    }

    /// <summary>
    /// Subscribe to messages for a specific connection
    /// </summary>
    public ChannelReader<string> Subscribe(string connectionId)
    {
        if (_connectionToPartition.TryGetValue(connectionId, out var partitionIndex))
        {
            return _partitions[partitionIndex].Subscribe(connectionId);
        }

        // Return empty completed channel if connection not found
        var emptyChannel = Channel.CreateUnbounded<string>();
        emptyChannel.Writer.Complete();

        _logger.LogWarning(
            "Attempted to subscribe to non-existent connection {ConnectionId}",
            connectionId
        );
        return emptyChannel.Reader;
    }

    /// <summary>
    /// Get comprehensive statistics about connections and performance
    /// Group statistics are now available through IGroupNotificationService
    /// </summary>
    public Task<ConnectionStats> GetStatsAsync()
    {
        var now = DateTime.UtcNow;
        var timeDiff = (now - _lastStatsUpdate).TotalSeconds;
        var messagesPerSecond = timeDiff > 0 ? (long)(_messageCounter / timeDiff) : 0;

        var stats = new ConnectionStats(
            TotalConnections: _connectionToPartition.Count,
            ActiveConnections: _partitions.Sum(p => p.ConnectionCount),
            MessagesPerSecond: messagesPerSecond,
            LastUpdated: now
        );

        _lastStatsUpdate = now;
        Interlocked.Exchange(ref _messageCounter, 0);

        return Task.FromResult(stats);
    }

    /// <summary>
    /// Background cleanup of disconnected clients across all partitions
    /// </summary>
    private void CleanupDisconnectedClients(object? state)
    {
        try
        {
            // Clean up disconnected clients from partitions
            Parallel.ForEach(_partitions, partition => partition.CleanupDisconnectedClients());

            // Get all currently active connection IDs from all partitions
            var activeConnectionIds = _partitions
                .SelectMany(partition => partition.GetConnectionIds())
                .ToHashSet();

            // Notify connection event service about cleanup
            _ = Task.Run(async () =>
            {
                try
                {
                    await _connectionEventService.OnConnectionsCleanupAsync(activeConnectionIds);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error during connection cleanup event handling");
                }
            });

            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug(
                    "Completed cleanup cycle across all partitions with {ActiveConnections} active connections",
                    activeConnectionIds.Count
                );
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during cleanup cycle");
        }
    }

    public void Dispose()
    {
        _logger.LogInformation("Disposing ScalableNotificationService...");

        _cleanupTimer?.Dispose();

        foreach (var partition in _partitions)
        {
            partition.Dispose();
        }

        _logger.LogInformation("ScalableNotificationService disposed successfully");
        GC.SuppressFinalize(this);
    }
}
