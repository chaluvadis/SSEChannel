namespace SSEChannel.Core.Backplane;

/// <summary>Abstraction for cross-instance message broadcasting.</summary>
public interface IChannelBackplane : IAsyncDisposable
{
    /// <summary>Subscribe to messages for a channel. The handler is called when a message arrives.</summary>
    Task SubscribeAsync(string channelName, Func<string, Task> handler, CancellationToken ct = default);

    /// <summary>Publish a message to all instances subscribed to a channel.</summary>
    Task PublishAsync(string channelName, string message, CancellationToken ct = default);

    /// <summary>Unsubscribe from a channel.</summary>
    Task UnsubscribeAsync(string channelName, CancellationToken ct = default);
}
