using System.Text.Json;
using SSEChannel.Core.Backplane;
using SSEChannel.Core.Connections;
using SSEChannel.Core.Models;

namespace SSEChannel.Core.Channels;

/// <summary>
/// Production-ready ISseChannel implementation with heartbeats, event replay, and backplane support.
/// </summary>
public sealed class SseChannelService : ISseChannel, IHostedService, IDisposable
{
    private readonly SseClientStore _clientStore;
    private readonly IChannelBackplane _backplane;
    private readonly SseOptions _options;
    private readonly ILogger<SseChannelService> _logger;
    private readonly JsonSerializerOptions _jsonOptions;
    // Tracks which channels already have a backplane subscription to avoid duplicates.
    private readonly ConcurrentDictionary<string, bool> _backplaneSubscriptions = new();
    private Timer? _heartbeatTimer;
    private readonly CancellationTokenSource _cts = new();

    public SseChannelService(
        SseClientStore clientStore,
        IChannelBackplane backplane,
        SseOptions options,
        ILogger<SseChannelService> logger)
    {
        _clientStore = clientStore;
        _backplane = backplane;
        _options = options;
        _logger = logger;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false,
        };
    }

    // ── IHostedService ────────────────────────────────────────────────────────

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _heartbeatTimer = new Timer(SendHeartbeats, null,
            _options.HeartbeatInterval, _options.HeartbeatInterval);
        _logger.LogInformation("SseChannelService started. Heartbeat: {Interval}",
            _options.HeartbeatInterval);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _heartbeatTimer?.Change(Timeout.Infinite, Timeout.Infinite);
        _cts.Cancel();
        return Task.CompletedTask;
    }

    // ── ISseChannel ───────────────────────────────────────────────────────────

    public async Task PublishAsync<T>(string channel, T message, string? eventName = null,
        CancellationToken ct = default)
    {
        ValidateChannelName(channel);

        var envelope = new SseMessage<T>
        {
            Channel = channel,
            EventName = eventName ?? "message",
            Payload = message,
        };

        string serialized;
        try { serialized = JsonSerializer.Serialize(envelope.Payload, _jsonOptions); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to serialize payload for channel {Channel}", channel);
            return;
        }

        var stored = SseMessage.From(envelope, serialized);
        _clientStore.GetOrCreateReplayBuffer(channel).Add(stored);

        var raw = FormatSseEvent(stored.Id, stored.EventName, serialized);

        // Ensure a backplane subscription is registered for this channel so that
        // published messages are delivered to local clients via the handler.
        await EnsureBackplaneSubscriptionAsync(channel);
        await _backplane.PublishAsync(channel, raw, ct);
    }

    public async Task SubscribeAsync(string channel, HttpContext context)
    {
        ValidateChannelName(channel);
        ConfigureSseHeaders(context);

        var lastEventId = context.Request.Headers["Last-Event-ID"].FirstOrDefault();
        var ct = context.RequestAborted;
        var clientId = Guid.NewGuid().ToString("N");

        // Ensure backplane subscription exists for this channel before accepting the client.
        await EnsureBackplaneSubscriptionAsync(channel);

        var client = _clientStore.AddClient(channel, clientId);
        _logger.LogInformation("SSE client {ClientId} subscribed to channel '{Channel}'", clientId, channel);

        try
        {
            // Replay missed messages from the buffer.
            var replay = _clientStore.GetOrCreateReplayBuffer(channel).GetSince(lastEventId);
            foreach (var msg in replay)
            {
                var replayLine = FormatSseEvent(msg.Id, msg.EventName, msg.SerializedPayload);
                await WriteAndFlushAsync(context, replayLine, ct);
            }

            // Stream real-time messages until client disconnects.
            await foreach (var raw in client.Reader.ReadAllAsync(ct))
            {
                await WriteAndFlushAsync(context, raw, ct);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("SSE client {ClientId} disconnected from '{Channel}'", clientId, channel);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SSE error for client {ClientId} on channel '{Channel}'", clientId, channel);
        }
        finally
        {
            _clientStore.RemoveClient(channel, clientId);
        }
    }

    public Task SendFromClientAsync<T>(string channel, T message, string? eventName = null,
        CancellationToken ct = default) =>
        PublishAsync(channel, message, eventName, ct);

    public int GetClientCount(string channel) => _clientStore.GetClientCount(channel);

    public IReadOnlyCollection<string> GetChannels() => _clientStore.GetChannels();

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Registers a single backplane handler per channel (idempotent).
    /// The handler broadcasts received messages to all local clients on that channel.
    /// </summary>
    private async Task EnsureBackplaneSubscriptionAsync(string channel)
    {
        if (_backplaneSubscriptions.TryAdd(channel, true))
        {
            await _backplane.SubscribeAsync(channel, msg =>
            {
                _clientStore.Broadcast(channel, msg);
                return Task.CompletedTask;
            }, _cts.Token);
        }
    }

    private void SendHeartbeats(object? _)
    {
        const string ping = ": ping\n\n";
        foreach (var ch in _clientStore.GetChannels())
            _clientStore.Broadcast(ch, ping);
    }

    private static string FormatSseEvent(string id, string eventName, string data) =>
        $"id: {id}\nevent: {eventName}\ndata: {data}\n\n";

    private static void ConfigureSseHeaders(HttpContext context)
    {
        context.Response.Headers.ContentType = "text/event-stream";
        context.Response.Headers.CacheControl = "no-cache, no-store, must-revalidate";
        context.Response.Headers["X-Accel-Buffering"] = "no";
        if (context.Request.Protocol == "HTTP/1.1")
            context.Response.Headers.Connection = "keep-alive";
    }

    private static async Task WriteAndFlushAsync(HttpContext context, string text, CancellationToken ct)
    {
        await context.Response.WriteAsync(text, ct);
        await context.Response.Body.FlushAsync(ct);
    }

    private void ValidateChannelName(string channel)
    {
        if (string.IsNullOrWhiteSpace(channel))
            throw new ArgumentException("Channel name must not be empty.", nameof(channel));
        if (channel.Length > _options.MaxChannelNameLength)
            throw new ArgumentException(
                $"Channel name exceeds maximum length of {_options.MaxChannelNameLength}.", nameof(channel));
    }

    public void Dispose()
    {
        _heartbeatTimer?.Dispose();
        _cts.Dispose();
    }
}
