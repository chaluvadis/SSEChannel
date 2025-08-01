namespace SSEChannel.Core.Services;

/// <summary>
/// Service for handling connection lifecycle events and coordinating cleanup
/// </summary>
public class ConnectionEventService : IConnectionEventService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ConnectionEventService> _logger;

    public ConnectionEventService(
        IServiceProvider serviceProvider,
        ILogger<ConnectionEventService> logger
    )
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    /// <summary>
    /// Handle connection disconnection event
    /// </summary>
    public async ValueTask OnConnectionDisconnectedAsync(string connectionId)
    {
        try
        {
            // Get the group service and remove the connection from all groups
            var groupService = _serviceProvider.GetService<IGroupNotificationService>();
            if (groupService != null)
            {
                await groupService.RemoveFromAllGroupsAsync(connectionId);

                if (_logger.IsEnabled(LogLevel.Debug))
                {
                    _logger.LogDebug(
                        "Removed connection {ConnectionId} from all groups",
                        connectionId
                    );
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error handling connection disconnection for {ConnectionId}",
                connectionId
            );
        }
    }

    /// <summary>
    /// Handle bulk connection cleanup event
    /// </summary>
    public async ValueTask OnConnectionsCleanupAsync(IEnumerable<string> activeConnectionIds)
    {
        try
        {
            // Get the group service and clean up orphaned connections
            var groupService = _serviceProvider.GetService<IGroupNotificationService>();
            if (groupService != null)
            {
                await groupService.CleanupOrphanedConnectionsAsync(activeConnectionIds);

                if (_logger.IsEnabled(LogLevel.Debug))
                {
                    _logger.LogDebug("Cleaned up orphaned connections from groups");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling bulk connection cleanup");
        }
    }
}
