namespace SSEChannel.Core.Services.Interfaces;

/// <summary>
/// Service for handling connection lifecycle events
/// </summary>
public interface IConnectionEventService
{
    /// <summary>
    /// Handle connection disconnection event
    /// </summary>
    ValueTask OnConnectionDisconnectedAsync(string connectionId);

    /// <summary>
    /// Handle bulk connection cleanup event
    /// </summary>
    ValueTask OnConnectionsCleanupAsync(IEnumerable<string> activeConnectionIds);
}
