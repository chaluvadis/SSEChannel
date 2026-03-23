namespace SSEChannel.Core.Backplane;

/// <summary>In-process backplane using Channels. For single-instance deployments.</summary>
public sealed class InMemoryChannelBackplane : IChannelBackplane
{
    private readonly ConcurrentDictionary<string, List<Func<string, Task>>> _handlers = new();
    private readonly object _lock = new();

    public Task SubscribeAsync(string channelName, Func<string, Task> handler, CancellationToken ct = default)
    {
        lock (_lock)
        {
            var list = _handlers.GetOrAdd(channelName, _ => []);
            list.Add(handler);
        }
        return Task.CompletedTask;
    }

    public async Task PublishAsync(string channelName, string message, CancellationToken ct = default)
    {
        List<Func<string, Task>>? handlers;
        lock (_lock)
        {
            if (!_handlers.TryGetValue(channelName, out handlers)) return;
            handlers = [.. handlers]; // snapshot
        }
        foreach (var handler in handlers)
        {
            try { await handler(message); }
            catch { /* individual handler failures must not break others */ }
        }
    }

    public Task UnsubscribeAsync(string channelName, CancellationToken ct = default)
    {
        lock (_lock) _handlers.TryRemove(channelName, out _);
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        lock (_lock) _handlers.Clear();
        return ValueTask.CompletedTask;
    }
}
