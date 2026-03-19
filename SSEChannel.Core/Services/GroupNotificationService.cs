namespace SSEChannel.Core.Services;

/// <summary>
/// Dedicated service for managing notification groups and group-based messaging
/// Extracted from ScalableNotificationServiceV2 for better separation of concerns
/// </summary>
public class GroupNotificationService : IGroupNotificationService, IDisposable
{
    private readonly ConcurrentDictionary<string, HashSet<string>> _groups = new();
    private readonly ConcurrentDictionary<string, HashSet<string>> _connectionGroups = new();
    private readonly SemaphoreSlim _groupLock = new(1, 1);
    private readonly ILogger<GroupNotificationService> _logger;
    private readonly Func<string, string, ValueTask> _publishToConnection;
    private readonly Timer? _cleanupTimer;

    // Performance tracking
    private long _messageCounter;
    private readonly DateTime _lastStatsUpdate = DateTime.UtcNow;

    // Cleanup tracking
    private long _totalCleanupOperations;
    private long _totalOrphanedConnectionsRemoved;
    private long _totalEmptyGroupsRemoved;

    public GroupNotificationService(
        Func<string, string, ValueTask> publishToConnection,
        ILogger<GroupNotificationService> logger,
        IConfiguration? configuration = null
    )
    {
        _publishToConnection = publishToConnection;
        _logger = logger;

        // Setup automatic cleanup of orphaned connections and empty groups
        var cleanupInterval = configuration?.GetValue("Groups:CleanupIntervalMs", 60000) ?? 60000; // Default 1 minute
        if (cleanupInterval > 0)
        {
            _cleanupTimer = new Timer(
                PerformCleanupCycle,
                null,
                TimeSpan.FromMilliseconds(cleanupInterval),
                TimeSpan.FromMilliseconds(cleanupInterval)
            );

            _logger.LogInformation(
                "GroupNotificationService initialized with cleanup interval: {CleanupInterval}ms",
                cleanupInterval
            );
        }
        else
        {
            _logger.LogInformation("GroupNotificationService initialized with cleanup disabled");
        }
    }

    /// <summary>
    /// Add a connection to a named group for targeted messaging
    /// </summary>
    public async ValueTask JoinGroupAsync(string connectionId, string groupName)
    {
        if (string.IsNullOrWhiteSpace(connectionId) || string.IsNullOrWhiteSpace(groupName))
        {
            _logger.LogWarning("Invalid connectionId or groupName provided for join operation");
            return;
        }

        await _groupLock.WaitAsync();
        try
        {
            // Add to group -> connections mapping
            if (!_groups.TryGetValue(groupName, out var connections))
            {
                connections = new HashSet<string>();
                _groups[groupName] = connections;
            }
            var wasAddedToGroup = connections.Add(connectionId);

            // Add to connection -> groups mapping
            if (!_connectionGroups.TryGetValue(connectionId, out var groups))
            {
                groups = new HashSet<string>();
                _connectionGroups[connectionId] = groups;
            }
            var wasAddedToConnection = groups.Add(groupName);

            if (wasAddedToGroup && wasAddedToConnection)
            {
                if (_logger.IsEnabled(LogLevel.Debug))
                {
                    _logger.LogDebug(
                        "Client {ConnectionId} joined group {GroupName}",
                        connectionId,
                        groupName
                    );
                }
            }
            else if (!wasAddedToGroup)
            {
                if (_logger.IsEnabled(LogLevel.Debug))
                {
                    _logger.LogDebug(
                        "Client {ConnectionId} was already in group {GroupName}",
                        connectionId,
                        groupName
                    );
                }
            }
        }
        finally
        {
            _groupLock.Release();
        }
    }

    /// <summary>
    /// Remove a connection from a named group
    /// </summary>
    public async ValueTask LeaveGroupAsync(string connectionId, string groupName)
    {
        if (string.IsNullOrWhiteSpace(connectionId) || string.IsNullOrWhiteSpace(groupName))
        {
            _logger.LogWarning("Invalid connectionId or groupName provided for leave operation");
            return;
        }

        await _groupLock.WaitAsync();
        try
        {
            var wasRemovedFromGroup = false;
            var wasRemovedFromConnection = false;

            // Remove from group -> connections mapping
            if (_groups.TryGetValue(groupName, out var connections))
            {
                wasRemovedFromGroup = connections.Remove(connectionId);

                // Clean up empty groups
                if (connections.Count == 0)
                {
                    _groups.TryRemove(groupName, out _);
                    if (_logger.IsEnabled(LogLevel.Debug))
                    {
                        _logger.LogDebug("Removed empty group {GroupName}", groupName);
                    }
                }
            }

            // Remove from connection -> groups mapping
            if (_connectionGroups.TryGetValue(connectionId, out var groups))
            {
                wasRemovedFromConnection = groups.Remove(groupName);

                // Clean up empty connection entries
                if (groups.Count == 0)
                {
                    _connectionGroups.TryRemove(connectionId, out _);
                }
            }

            if (wasRemovedFromGroup && wasRemovedFromConnection)
            {
                if (_logger.IsEnabled(LogLevel.Debug))
                {
                    _logger.LogDebug(
                        "Client {ConnectionId} left group {GroupName}",
                        connectionId,
                        groupName
                    );
                }
            }
            else if (!wasRemovedFromGroup)
            {
                if (_logger.IsEnabled(LogLevel.Debug))
                {
                    _logger.LogDebug(
                        "Client {ConnectionId} was not in group {GroupName}",
                        connectionId,
                        groupName
                    );
                }
            }
        }
        finally
        {
            _groupLock.Release();
        }
    }

    /// <summary>
    /// Remove a connection from all groups (called during disconnect)
    /// </summary>
    public async ValueTask RemoveFromAllGroupsAsync(string connectionId)
    {
        if (string.IsNullOrWhiteSpace(connectionId))
        {
            return;
        }

        await _groupLock.WaitAsync();
        try
        {
            // Get all groups for this connection
            if (!_connectionGroups.TryRemove(connectionId, out var groups))
            {
                return; // Connection wasn't in any groups
            }

            var groupsToCleanup = new List<string>();
            var removedFromGroupCount = 0;

            // Remove connection from all its groups
            foreach (var groupName in groups)
            {
                if (_groups.TryGetValue(groupName, out var connections))
                {
                    if (connections.Remove(connectionId))
                    {
                        removedFromGroupCount++;
                    }

                    // Mark empty groups for cleanup
                    if (connections.Count == 0)
                    {
                        groupsToCleanup.Add(groupName);
                    }
                }
            }

            // Remove empty groups
            foreach (var groupName in groupsToCleanup)
            {
                _groups.TryRemove(groupName, out _);
            }

            if (removedFromGroupCount > 0)
            {
                if (_logger.IsEnabled(LogLevel.Debug))
                {
                    _logger.LogDebug(
                        "Removed connection {ConnectionId} from {GroupCount} groups, cleaned up {EmptyGroups} empty groups",
                        connectionId,
                        removedFromGroupCount,
                        groupsToCleanup.Count
                    );
                }
            }
        }
        finally
        {
            _groupLock.Release();
        }
    }

    /// <summary>
    /// Send message to all connections in a specific group
    /// </summary>
    public async ValueTask PublishToGroupAsync(string groupName, string message)
    {
        if (string.IsNullOrWhiteSpace(groupName) || string.IsNullOrWhiteSpace(message))
        {
            _logger.LogWarning("Invalid groupName or message provided for group publish");
            return;
        }

        HashSet<string> connections;

        // Get group members safely
        await _groupLock.WaitAsync();
        try
        {
            if (!_groups.TryGetValue(groupName, out connections!))
            {
                if (_logger.IsEnabled(LogLevel.Debug))
                {
                    _logger.LogDebug(
                        "Attempted to publish to non-existent group {GroupName}",
                        groupName
                    );
                }
                return;
            }

            // Create a copy to avoid enumeration issues
            connections = new HashSet<string>(connections);
        }
        finally
        {
            _groupLock.Release();
        }

        if (connections.Count == 0)
        {
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug("Group {GroupName} has no members", groupName);
            }
            return;
        }

        // Process connections in batches to avoid overwhelming the system
        const int batchSize = 1000;
        var connectionArray = connections.ToArray();

        for (int i = 0; i < connectionArray.Length; i += batchSize)
        {
            var batch = connectionArray.Skip(i).Take(batchSize);
            var tasks = batch.Select(connectionId =>
                _publishToConnection(connectionId, message).AsTask()
            );

            await Task.WhenAll(tasks);
        }

        Interlocked.Add(ref _messageCounter, connections.Count);

        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug(
                "Published message to group {GroupName} with {ConnectionCount} members",
                groupName,
                connections.Count
            );
        }
    }

    /// <summary>
    /// Get all groups that a connection belongs to
    /// </summary>
    public async Task<IReadOnlyList<string>> GetGroupsForConnectionAsync(string connectionId)
    {
        if (string.IsNullOrWhiteSpace(connectionId))
        {
            return Array.Empty<string>();
        }

        await _groupLock.WaitAsync();
        try
        {
            if (_connectionGroups.TryGetValue(connectionId, out var groups))
            {
                return groups.ToArray();
            }
            return Array.Empty<string>();
        }
        finally
        {
            _groupLock.Release();
        }
    }

    /// <summary>
    /// Get all connections in a specific group
    /// </summary>
    public async Task<IReadOnlyList<string>> GetConnectionsInGroupAsync(string groupName)
    {
        if (string.IsNullOrWhiteSpace(groupName))
        {
            return Array.Empty<string>();
        }

        await _groupLock.WaitAsync();
        try
        {
            if (_groups.TryGetValue(groupName, out var connections))
            {
                return connections.ToArray();
            }
            return Array.Empty<string>();
        }
        finally
        {
            _groupLock.Release();
        }
    }

    /// <summary>
    /// Get statistics about all groups
    /// </summary>
    public async Task<IReadOnlyDictionary<string, int>> GetGroupStatisticsAsync()
    {
        await _groupLock.WaitAsync();
        try
        {
            return _groups.ToDictionary(g => g.Key, g => g.Value.Count);
        }
        finally
        {
            _groupLock.Release();
        }
    }

    /// <summary>
    /// Get the number of members in a specific group
    /// </summary>
    public async Task<int> GetGroupMemberCountAsync(string groupName)
    {
        if (string.IsNullOrWhiteSpace(groupName))
        {
            return 0;
        }

        await _groupLock.WaitAsync();
        try
        {
            if (_groups.TryGetValue(groupName, out var connections))
            {
                return connections.Count;
            }
            return 0;
        }
        finally
        {
            _groupLock.Release();
        }
    }

    /// <summary>
    /// Check if a group exists
    /// </summary>
    public async Task<bool> GroupExistsAsync(string groupName)
    {
        if (string.IsNullOrWhiteSpace(groupName))
        {
            return false;
        }

        await _groupLock.WaitAsync();
        try
        {
            return _groups.ContainsKey(groupName);
        }
        finally
        {
            _groupLock.Release();
        }
    }

    /// <summary>
    /// Get all group names
    /// </summary>
    public async Task<IReadOnlyList<string>> GetAllGroupsAsync()
    {
        await _groupLock.WaitAsync();
        try
        {
            return [.. _groups.Keys];
        }
        finally
        {
            _groupLock.Release();
        }
    }

    /// <summary>
    /// Remove empty groups (maintenance operation)
    /// </summary>
    public async ValueTask RemoveEmptyGroupsAsync()
    {
        await _groupLock.WaitAsync();
        try
        {
            var emptyGroups = _groups.Where(g => g.Value.Count == 0).Select(g => g.Key).ToArray();

            foreach (var groupName in emptyGroups)
            {
                _groups.TryRemove(groupName, out _);
            }

            if (emptyGroups.Length > 0)
            {
                Interlocked.Add(ref _totalEmptyGroupsRemoved, emptyGroups.Length);
                _logger.LogInformation(
                    "Removed {EmptyGroupCount} empty groups",
                    emptyGroups.Length
                );
            }
        }
        finally
        {
            _groupLock.Release();
        }
    }

    /// <summary>
    /// Remove orphaned connections from groups (connections that no longer exist)
    /// This should be called with a list of currently active connection IDs
    /// </summary>
    public async ValueTask CleanupOrphanedConnectionsAsync(IEnumerable<string> activeConnectionIds)
    {
        if (activeConnectionIds == null)
        {
            _logger.LogWarning(
                "CleanupOrphanedConnectionsAsync called with null activeConnectionIds"
            );
            return;
        }

        var activeConnections = new HashSet<string>(activeConnectionIds);
        var orphanedConnections = new List<string>();
        var groupsToCleanup = new List<string>();

        await _groupLock.WaitAsync();
        try
        {
            // Find orphaned connections
            foreach (var connectionId in _connectionGroups.Keys)
            {
                if (!activeConnections.Contains(connectionId))
                {
                    orphanedConnections.Add(connectionId);
                }
            }

            // Remove orphaned connections from all groups
            foreach (var connectionId in orphanedConnections)
            {
                if (_connectionGroups.TryRemove(connectionId, out var groups))
                {
                    foreach (var groupName in groups)
                    {
                        if (_groups.TryGetValue(groupName, out var groupConnections))
                        {
                            groupConnections.Remove(connectionId);

                            // Mark empty groups for cleanup
                            if (groupConnections.Count == 0)
                            {
                                groupsToCleanup.Add(groupName);
                            }
                        }
                    }
                }
            }

            // Remove empty groups
            foreach (var groupName in groupsToCleanup)
            {
                _groups.TryRemove(groupName, out _);
            }

            if (orphanedConnections.Count > 0)
            {
                Interlocked.Add(ref _totalOrphanedConnectionsRemoved, orphanedConnections.Count);
                Interlocked.Add(ref _totalEmptyGroupsRemoved, groupsToCleanup.Count);

                _logger.LogInformation(
                    "Cleaned up {OrphanedCount} orphaned connections and {EmptyGroupCount} empty groups",
                    orphanedConnections.Count,
                    groupsToCleanup.Count
                );
            }
        }
        finally
        {
            _groupLock.Release();
        }
    }

    /// <summary>
    /// Perform comprehensive cleanup of groups and connections
    /// </summary>
    public async ValueTask PerformComprehensiveCleanupAsync(
        IEnumerable<string>? activeConnectionIds = null
    )
    {
        Interlocked.Increment(ref _totalCleanupOperations);

        try
        {
            // First, clean up orphaned connections if active connection list is provided
            if (activeConnectionIds != null)
            {
                await CleanupOrphanedConnectionsAsync(activeConnectionIds);
            }

            // Then, clean up any remaining empty groups
            await RemoveEmptyGroupsAsync();

            if (_logger.IsEnabled(LogLevel.Debug))
            {
                var stats = await GetGroupStatisticsAsync();
                _logger.LogDebug(
                    "Comprehensive cleanup completed. Active groups: {GroupCount}, Total connections in groups: {ConnectionCount}",
                    stats.Count,
                    stats.Values.Sum()
                );
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during comprehensive group cleanup");
        }
    }

    /// <summary>
    /// Get cleanup statistics for monitoring and diagnostics
    /// </summary>
    public Task<GroupCleanupStats> GetCleanupStatsAsync()
    {
        var stats = new GroupCleanupStats(
            TotalCleanupOperations: _totalCleanupOperations,
            TotalOrphanedConnectionsRemoved: _totalOrphanedConnectionsRemoved,
            TotalEmptyGroupsRemoved: _totalEmptyGroupsRemoved,
            LastUpdated: DateTime.UtcNow
        );

        return Task.FromResult(stats);
    }

    /// <summary>
    /// Reset cleanup statistics (useful for monitoring)
    /// </summary>
    public void ResetCleanupStats()
    {
        Interlocked.Exchange(ref _totalCleanupOperations, 0);
        Interlocked.Exchange(ref _totalOrphanedConnectionsRemoved, 0);
        Interlocked.Exchange(ref _totalEmptyGroupsRemoved, 0);

        _logger.LogInformation("Group cleanup statistics reset");
    }

    /// <summary>
    /// Background cleanup cycle - called by timer
    /// </summary>
    private void PerformCleanupCycle(object? state)
    {
        try
        {
            // Perform basic empty group cleanup
            _ = Task.Run(async () => await RemoveEmptyGroupsAsync());

            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug("Completed group cleanup cycle");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during group cleanup cycle");
        }
    }

    public void Dispose()
    {
        _logger.LogInformation("Disposing GroupNotificationService...");

        _cleanupTimer?.Dispose();
        _groupLock?.Dispose();

        _logger.LogInformation("GroupNotificationService disposed successfully");
        GC.SuppressFinalize(this);
    }
}
