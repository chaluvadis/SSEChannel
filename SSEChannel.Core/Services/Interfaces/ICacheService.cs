namespace SSEChannel.Core.Services.Interfaces;

/// <summary>
/// Interface for caching service operations
/// </summary>
public interface ICacheService
{
    /// <summary>
    /// Get cached connection statistics
    /// </summary>
    Task<ConnectionStats?> GetConnectionStatsAsync();

    /// <summary>
    /// Cache connection statistics
    /// </summary>
    Task SetConnectionStatsAsync(ConnectionStats stats, TimeSpan? expiration = null);

    /// <summary>
    /// Get cached group statistics
    /// </summary>
    Task<Dictionary<string, int>?> GetGroupStatisticsAsync();

    /// <summary>
    /// Cache group statistics
    /// </summary>
    Task SetGroupStatisticsAsync(Dictionary<string, int> stats, TimeSpan? expiration = null);

    /// <summary>
    /// Get cached group member count
    /// </summary>
    Task<int?> GetGroupMemberCountAsync(string groupName);

    /// <summary>
    /// Cache group member count
    /// </summary>
    Task SetGroupMemberCountAsync(string groupName, int count, TimeSpan? expiration = null);

    /// <summary>
    /// Invalidate cache entries matching a pattern
    /// </summary>
    Task InvalidateAsync(string pattern);

    /// <summary>
    /// Get value from cache or set it using the factory function
    /// </summary>
    Task<T?> GetOrSetAsync<T>(string key, Func<Task<T>> factory, TimeSpan? expiration = null)
        where T : class;
}
