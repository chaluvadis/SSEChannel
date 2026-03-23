namespace SSEChannel.Core.Channels;

/// <summary>Unified SSE channel abstraction for bidirectional communication.</summary>
public interface ISseChannel
{
    /// <summary>Publish a message to all subscribers of a channel (server → clients).</summary>
    Task PublishAsync<T>(string channel, T message, string? eventName = null, CancellationToken ct = default);

    /// <summary>Subscribe the current HTTP connection to a channel (long-lived SSE stream).</summary>
    Task SubscribeAsync(string channel, HttpContext context);

    /// <summary>Receive a message from a client and broadcast it to the channel (client → server → all clients).</summary>
    Task SendFromClientAsync<T>(string channel, T message, string? eventName = null, CancellationToken ct = default);

    /// <summary>Get number of connected clients for a channel.</summary>
    int GetClientCount(string channel);

    /// <summary>Get all active channel names.</summary>
    IReadOnlyCollection<string> GetChannels();
}
