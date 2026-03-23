namespace SSEChannel.Core.Models;

public class SseOptions
{
    public const string SectionName = "SseChannel";

    /// <summary>Heartbeat interval (default: 20 seconds)</summary>
    public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(20);

    /// <summary>Max messages to replay per channel on reconnect (default: 100)</summary>
    public int ReplayBufferSize { get; set; } = 100;

    /// <summary>Max channel name length</summary>
    public int MaxChannelNameLength { get; set; } = 64;

    /// <summary>Use Redis backplane (default: false)</summary>
    public bool UseRedisBackplane { get; set; } = false;

    /// <summary>Redis connection string (used when UseRedisBackplane=true)</summary>
    public string? RedisConnectionString { get; set; }
}
