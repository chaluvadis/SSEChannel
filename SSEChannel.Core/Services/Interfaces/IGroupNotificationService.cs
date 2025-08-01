namespace SSEChannel.Core.Services.Interfaces;

/// <summary>
/// Service interface for managing notification groups and group-based messaging
/// </summary>
public interface IGroupNotificationService
{
    // Group membership management
    ValueTask JoinGroupAsync(string connectionId, string groupName);
    ValueTask LeaveGroupAsync(string connectionId, string groupName);
    ValueTask RemoveFromAllGroupsAsync(string connectionId);

    // Group messaging
    ValueTask PublishToGroupAsync(string groupName, string message);

    // Group information and statistics
    Task<IReadOnlyList<string>> GetGroupsForConnectionAsync(string connectionId);
    Task<IReadOnlyList<string>> GetConnectionsInGroupAsync(string groupName);
    Task<IReadOnlyDictionary<string, int>> GetGroupStatisticsAsync();
    Task<int> GetGroupMemberCountAsync(string groupName);

    // Group management
    Task<bool> GroupExistsAsync(string groupName);
    Task<IReadOnlyList<string>> GetAllGroupsAsync();
    ValueTask RemoveEmptyGroupsAsync();

    // Group cleanup operations
    ValueTask CleanupOrphanedConnectionsAsync(IEnumerable<string> activeConnectionIds);
    ValueTask PerformComprehensiveCleanupAsync(IEnumerable<string>? activeConnectionIds = null);
    Task<GroupCleanupStats> GetCleanupStatsAsync();
    void ResetCleanupStats();
}

/// <summary>
/// Statistics about group operations
/// </summary>
public record GroupStats(
    int TotalGroups,
    int TotalMemberships,
    Dictionary<string, int> GroupMemberCounts,
    DateTime LastUpdated
);

/// <summary>
/// Statistics about group cleanup operations
/// </summary>
public record GroupCleanupStats(
    long TotalCleanupOperations,
    long TotalOrphanedConnectionsRemoved,
    long TotalEmptyGroupsRemoved,
    DateTime LastUpdated
);
