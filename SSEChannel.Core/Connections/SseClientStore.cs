using SSEChannel.Core.Models;

namespace SSEChannel.Core.Connections;

/// <summary>Thread-safe store of SSE clients indexed by channel name.</summary>
public sealed class SseClientStore
{
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, SseClient>> _channels = new();
    private readonly ConcurrentDictionary<string, ReplayBuffer> _replayBuffers = new();
    private readonly int _replayBufferSize;

    public SseClientStore(int replayBufferSize = 100)
    {
        _replayBufferSize = replayBufferSize;
    }

    public SseClient AddClient(string channelName, string? clientId = null)
    {
        clientId ??= Guid.NewGuid().ToString("N");
        var clients = _channels.GetOrAdd(channelName, _ => new ConcurrentDictionary<string, SseClient>());
        var client = new SseClient(clientId, channelName);
        clients[clientId] = client;
        return client;
    }

    public bool RemoveClient(string channelName, string clientId)
    {
        if (_channels.TryGetValue(channelName, out var clients))
        {
            if (clients.TryRemove(clientId, out var client))
            {
                client.Dispose();
                return true;
            }
        }
        return false;
    }

    public IEnumerable<SseClient> GetClients(string channelName)
    {
        if (_channels.TryGetValue(channelName, out var clients))
            return clients.Values;
        return [];
    }

    public int GetClientCount(string channelName) =>
        _channels.TryGetValue(channelName, out var clients) ? clients.Count : 0;

    public IReadOnlyCollection<string> GetChannels() => [.. _channels.Keys];

    public ReplayBuffer GetOrCreateReplayBuffer(string channelName) =>
        _replayBuffers.GetOrAdd(channelName, _ => new ReplayBuffer(_replayBufferSize));

    public void Broadcast(string channelName, string message)
    {
        if (_channels.TryGetValue(channelName, out var clients))
        {
            foreach (var client in clients.Values)
                client.TryWrite(message);
        }
    }
}
