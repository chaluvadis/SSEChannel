using StackExchange.Redis;

namespace SSEChannel.Core.Backplane;

/// <summary>
/// Redis-backed backplane for multi-instance SSE broadcasting via pub/sub.
/// Register this when UseRedisBackplane=true and RedisConnectionString is set.
/// </summary>
public sealed class RedisChannelBackplane : IChannelBackplane
{
    private readonly ISubscriber _subscriber;
    private readonly ConcurrentDictionary<string, Func<RedisChannel, RedisValue, Task>> _subscriptions = new();
    private readonly ILogger<RedisChannelBackplane> _logger;

    public RedisChannelBackplane(IConnectionMultiplexer redis, ILogger<RedisChannelBackplane> logger)
    {
        _subscriber = redis.GetSubscriber();
        _logger = logger;
    }

    public async Task SubscribeAsync(string channelName, Func<string, Task> handler, CancellationToken ct = default)
    {
        var redisChannel = new RedisChannel(channelName, RedisChannel.PatternMode.Literal);
        Func<RedisChannel, RedisValue, Task> redisHandler = async (_, value) =>
        {
            try { await handler(value.ToString()); }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Redis handler error for channel {Channel}", channelName);
            }
        };
        _subscriptions[channelName] = redisHandler;
        await _subscriber.SubscribeAsync(redisChannel, (ch, val) => _ = redisHandler(ch, val));
    }

    public async Task PublishAsync(string channelName, string message, CancellationToken ct = default)
    {
        await _subscriber.PublishAsync(
            new RedisChannel(channelName, RedisChannel.PatternMode.Literal), message);
    }

    public async Task UnsubscribeAsync(string channelName, CancellationToken ct = default)
    {
        if (_subscriptions.TryRemove(channelName, out _))
            await _subscriber.UnsubscribeAsync(
                new RedisChannel(channelName, RedisChannel.PatternMode.Literal));
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var key in _subscriptions.Keys)
            await _subscriber.UnsubscribeAsync(
                new RedisChannel(key, RedisChannel.PatternMode.Literal));
        _subscriptions.Clear();
    }
}
