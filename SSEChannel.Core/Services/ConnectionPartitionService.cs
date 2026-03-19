namespace SSEChannel.Core.Services;

/// <summary>
/// Individual partition to handle a subset of connections for better concurrency
/// </summary>
internal class ConnectionPartitionService(
    int partitionId,
    ILogger<ConnectionPartitionService>? logger = null
) : IConnectionPartitionService, IDisposable
{
    private readonly int _partitionId = partitionId;
    private readonly ConcurrentDictionary<string, Channel<string>> _connections = new();
    private readonly ILogger<ConnectionPartitionService>? _logger = logger;

    public int ConnectionCount => _connections.Count;
    public int PartitionId => _partitionId;

    /// <summary>
    /// Add a new connection to this partition
    /// </summary>
    public void AddConnection(string connectionId)
    {
        var channel = _connections.GetOrAdd(connectionId, _ => Channel.CreateUnbounded<string>());
        _logger?.LogDebug(
            "Added connection {ConnectionId} to partition {PartitionId}",
            connectionId,
            _partitionId
        );
    }

    /// <summary>
    /// Remove a connection from this partition
    /// </summary>
    public void RemoveConnection(string connectionId)
    {
        if (_connections.TryRemove(connectionId, out var channel))
        {
            channel.Writer.TryComplete();
            _logger?.LogDebug(
                "Removed connection {ConnectionId} from partition {PartitionId}",
                connectionId,
                _partitionId
            );
        }
    }

    /// <summary>
    /// Publish message to a specific connection in this partition
    /// </summary>
    public ValueTask PublishToConnectionAsync(string connectionId, string message)
    {
        if (_connections.TryGetValue(connectionId, out var channel))
        {
            try
            {
                return channel.Writer.WriteAsync(message);
            }
            catch (InvalidOperationException)
            {
                // Channel was closed, remove it
                RemoveConnection(connectionId);
            }
        }
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Publish message to all connections in this partition
    /// </summary>
    public async ValueTask PublishToAllAsync(string message)
    {
        var connections = _connections.Values.ToArray();

        if (connections.Length == 0)
            return;

        // Convert ValueTask to Task immediately to avoid storing ValueTask instances
        var tasks = connections
            .Select(channel => PublishToChannelSafeAsync(channel, message))
            .ToArray();

        await Task.WhenAll(tasks);

        _logger?.LogDebug(
            "Published message to {Count} connections in partition {PartitionId}",
            connections.Length,
            _partitionId
        );
    }

    /// <summary>
    /// Get channel reader for a specific connection
    /// </summary>
    public ChannelReader<string> Subscribe(string connectionId)
    {
        var channel = _connections.GetOrAdd(connectionId, _ => Channel.CreateUnbounded<string>());
        return channel.Reader;
    }

    /// <summary>
    /// Clean up disconnected clients in this partition
    /// </summary>
    public void CleanupDisconnectedClients()
    {
        var toRemove = new List<string>();

        foreach (var kvp in _connections)
        {
            if (kvp.Value.Reader.Completion.IsCompleted)
            {
                toRemove.Add(kvp.Key);
            }
        }

        foreach (var connectionId in toRemove)
        {
            RemoveConnection(connectionId);
        }

        if (toRemove.Count > 0)
        {
            _logger?.LogInformation(
                "Cleaned up {Count} disconnected clients from partition {PartitionId}",
                toRemove.Count,
                _partitionId
            );
        }
    }

    /// <summary>
    /// Get all connection IDs in this partition
    /// </summary>
    public IEnumerable<string> GetConnectionIds() => [.. _connections.Keys];

    /// <summary>
    /// Safely publish to a channel with error handling
    /// </summary>
    private async Task PublishToChannelSafeAsync(Channel<string> channel, string message)
    {
        try
        {
            await channel.Writer.WriteAsync(message);
        }
        catch (InvalidOperationException)
        {
            // Channel was closed, this is expected behavior
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(
                ex,
                "Error publishing to channel in partition {PartitionId}",
                _partitionId
            );
        }
    }

    public void Dispose()
    {
        foreach (var channel in _connections.Values)
        {
            channel.Writer.TryComplete();
        }
        _connections.Clear();

        _logger?.LogInformation(
            "Disposed partition {PartitionId} with {Count} connections",
            _partitionId,
            _connections.Count
        );
    }
}
