namespace SSEChannel.Core.Connections;

/// <summary>Represents a connected SSE client.</summary>
public sealed class SseClient : IDisposable
{
    private readonly Channel<string> _channel;
    private int _disposed;

    public string ClientId { get; }
    public string ChannelName { get; }
    public DateTimeOffset ConnectedAt { get; } = DateTimeOffset.UtcNow;
    public ChannelReader<string> Reader => _channel.Reader;

    public SseClient(string clientId, string channelName)
    {
        ClientId = clientId;
        ChannelName = channelName;
        _channel = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
        });
    }

    public bool TryWrite(string message) =>
        Interlocked.CompareExchange(ref _disposed, 0, 0) == 0 && _channel.Writer.TryWrite(message);

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
            _channel.Writer.TryComplete();
    }
}
