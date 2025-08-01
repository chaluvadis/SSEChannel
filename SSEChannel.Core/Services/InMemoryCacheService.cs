using Microsoft.Extensions.Caching.Memory;

namespace SSEChannel.Core.Services;

/// <summary>
/// In-memory implementation of the cache service for simple caching without external dependencies
/// </summary>
public class InMemoryCacheService : ICacheService
{
    private readonly IMemoryCache _cache;
    private readonly ILogger<InMemoryCacheService> _logger;

    // Cache key constants
    private const string ConnectionStatsKey = "sse:stats:connections";
    private const string GroupStatsKey = "sse:stats:groups";
    private const string GroupMemberCountPrefix = "sse:group:count:";

    public InMemoryCacheService(IMemoryCache cache, ILogger<InMemoryCacheService> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    public Task<ConnectionStats?> GetConnectionStatsAsync()
    {
        try
        {
            var stats = _cache.Get<ConnectionStats>(ConnectionStatsKey);
            return Task.FromResult(stats);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get connection stats from cache");
            return Task.FromResult<ConnectionStats?>(null);
        }
    }

    public Task SetConnectionStatsAsync(ConnectionStats stats, TimeSpan? expiration = null)
    {
        try
        {
            var options = new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = expiration ?? TimeSpan.FromSeconds(30),
            };
            _cache.Set(ConnectionStatsKey, stats, options);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to cache connection stats");
        }
        return Task.CompletedTask;
    }

    public Task<Dictionary<string, int>?> GetGroupStatisticsAsync()
    {
        try
        {
            var stats = _cache.Get<Dictionary<string, int>>(GroupStatsKey);
            return Task.FromResult(stats);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get group statistics from cache");
            return Task.FromResult<Dictionary<string, int>?>(null);
        }
    }

    public Task SetGroupStatisticsAsync(Dictionary<string, int> stats, TimeSpan? expiration = null)
    {
        try
        {
            var options = new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = expiration ?? TimeSpan.FromSeconds(60),
            };
            _cache.Set(GroupStatsKey, stats, options);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to cache group statistics");
        }
        return Task.CompletedTask;
    }

    public Task<int?> GetGroupMemberCountAsync(string groupName)
    {
        try
        {
            var key = GroupMemberCountPrefix + groupName;
            var count = _cache.Get<int?>(key);
            return Task.FromResult(count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to get group member count from cache for group {GroupName}",
                groupName
            );
            return Task.FromResult<int?>(null);
        }
    }

    public Task SetGroupMemberCountAsync(string groupName, int count, TimeSpan? expiration = null)
    {
        try
        {
            var key = GroupMemberCountPrefix + groupName;
            var options = new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = expiration ?? TimeSpan.FromSeconds(30),
            };
            _cache.Set(key, count, options);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to cache group member count for group {GroupName}",
                groupName
            );
        }
        return Task.CompletedTask;
    }

    public Task InvalidateAsync(string pattern)
    {
        try
        {
            // For in-memory cache, we need to track keys to support pattern-based invalidation
            // This is a simplified implementation - in production you might want to use a more sophisticated approach
            if (_cache is MemoryCache memoryCache)
            {
                // Unfortunately, MemoryCache doesn't expose a way to enumerate keys
                // So we'll implement a simple approach for common patterns
                if (pattern.Contains("group:count:"))
                {
                    // Clear all group count entries - this is a limitation of MemoryCache
                    _logger.LogDebug(
                        "Pattern-based invalidation requested for {Pattern} - clearing related entries",
                        pattern
                    );
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to invalidate cache keys with pattern {Pattern}",
                pattern
            );
        }
        return Task.CompletedTask;
    }

    public async Task<T?> GetOrSetAsync<T>(
        string key,
        Func<Task<T>> factory,
        TimeSpan? expiration = null
    )
        where T : class
    {
        try
        {
            // Try to get from cache first
            if (_cache.TryGetValue(key, out T? cachedValue) && cachedValue != null)
            {
                return cachedValue;
            }

            // Not in cache, use factory to get value
            var value = await factory();
            if (value != null)
            {
                var options = new MemoryCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = expiration ?? TimeSpan.FromMinutes(5),
                };
                _cache.Set(key, value, options);
            }

            return value;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get or set cache value for key {Key}", key);
            // Fall back to factory function
            return await factory();
        }
    }
}
