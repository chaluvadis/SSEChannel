namespace SSEChannel.Core.Services.Interfaces;

/// <summary>
/// High-performance notification service interface designed for millions of concurrent connections
/// Group operations have been moved to IGroupNotificationService for better separation of concerns
/// </summary>
public interface IScalableNotificationService
{
    // Connection management
    ValueTask<string> ConnectAsync(string? connectionId = null);
    ValueTask DisconnectAsync(string connectionId);

    // Publishing methods
    ValueTask PublishToConnectionAsync(string connectionId, string message);
    ValueTask PublishToAllAsync(string message);

    // Subscription for SSE
    ChannelReader<string> Subscribe(string connectionId);

    // Statistics and monitoring
    Task<ConnectionStats> GetStatsAsync();
}

/// <summary>
/// Connection statistics for monitoring and observability
/// Group statistics are now available through IGroupNotificationService
/// </summary>
public record ConnectionStats(
    int TotalConnections,
    int ActiveConnections,
    long MessagesPerSecond,
    DateTime LastUpdated
);
