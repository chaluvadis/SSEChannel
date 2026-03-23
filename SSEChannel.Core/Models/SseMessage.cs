namespace SSEChannel.Core.Models;

public class SseMessage<T>
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Channel { get; init; } = string.Empty;
    public string EventName { get; init; } = "message";
    public T? Payload { get; init; }
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>Non-generic version for storage and replay.</summary>
public class SseMessage
{
    public string Id { get; init; } = string.Empty;
    public string Channel { get; init; } = string.Empty;
    public string EventName { get; init; } = "message";
    public string SerializedPayload { get; init; } = string.Empty;
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    public static SseMessage From<T>(SseMessage<T> typed, string serializedPayload) => new()
    {
        Id = typed.Id,
        Channel = typed.Channel,
        EventName = typed.EventName,
        SerializedPayload = serializedPayload,
        Timestamp = typed.Timestamp,
    };
}
