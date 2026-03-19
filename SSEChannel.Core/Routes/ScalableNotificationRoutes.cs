using System.Text;

namespace SSEChannel.Core.Routes;

/// <summary>
/// High-performance SSE routes designed for millions of concurrent connections
/// </summary>
public static class ScalableNotificationRoutes
{
    public static void MapScalableNotificationRoutes(this WebApplication app)
    {
        // Core SSE streaming endpoint
        app.MapGet("/api/notifications/stream/{connectionId?}", StreamNotifications);

        // Connection management endpoints
        app.MapPost("/api/notifications/connect", ConnectClient);
        app.MapDelete("/api/notifications/{connectionId}", DisconnectClient);

        // Publishing endpoints
        app.MapPost("/api/notifications/send/{connectionId}", SendToConnection);
        app.MapPost("/api/notifications/broadcast/all", BroadcastToAll);

        // Monitoring and statistics endpoints
        app.MapGet("/api/notifications/stats", GetStats);
        app.MapGet("/api/notifications/health", GetHealth);
    }

    /// <summary>
    /// High-performance SSE streaming endpoint optimized for concurrent connections
    /// </summary>
    private static async Task StreamNotifications(
        HttpContext context,
        IScalableNotificationService notificationService,
        ILogger<Program> logger,
        string? connectionId = null
    )
    {
        // Configure SSE headers for optimal performance (HTTP/2 and HTTP/3 compatible)
        ConfigureSseHeaders(context);

        var clientIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        string? actualConnectionId = null;

        try
        {
            // Establish connection with automatic partition assignment
            actualConnectionId = await notificationService.ConnectAsync(connectionId);
            var reader = notificationService.Subscribe(actualConnectionId);

            // Optimized logging - only log if debug is enabled to reduce contention
            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation(
                    "SSE connection established: {ConnectionId} from {ClientIp}",
                    actualConnectionId,
                    clientIp
                );
            }

            // Batch initial messages to reduce write operations and improve performance
            var timestamp = DateTime.Now.ToString("HH:mm:ss");
            var initialMessages = new StringBuilder();
            initialMessages.AppendLine($"event: connected");
            initialMessages.AppendLine($"data: {actualConnectionId}");
            initialMessages.AppendLine();
            initialMessages.AppendLine($"event: message");
            initialMessages.AppendLine(
                $"data: Welcome! Connected as {actualConnectionId} at {timestamp}"
            );
            initialMessages.AppendLine();

            // Send all initial messages in one write operation
            await context.Response.WriteAsync(initialMessages.ToString());
            await context.Response.Body.FlushAsync();

            // Stream messages until client disconnects
            await foreach (var message in reader.ReadAllAsync(context.RequestAborted))
            {
                await context.Response.WriteAsync($"event: message\n");
                await context.Response.WriteAsync($"data: {message}\n\n");
                await context.Response.Body.FlushAsync();
            }
        }
        catch (OperationCanceledException)
        {
            // Client disconnected - this is normal behavior
            logger.LogDebug("SSE connection cancelled: {ClientIp}", clientIp);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "SSE connection error for {ClientIp}", clientIp);
        }
        finally
        {
            // Clean up connection
            if (connectionId != null)
            {
                await notificationService.DisconnectAsync(connectionId);
                logger.LogDebug("SSE connection cleaned up: {ConnectionId}", connectionId);
            }
        }
    }

    private static async Task<IResult> ConnectClient(
        IScalableNotificationService notificationService,
        ConnectRequest? request = null
    )
    {
        var connectionId = await notificationService.ConnectAsync(request?.ConnectionId);
        return Results.Ok(new { ConnectionId = connectionId, ConnectedAt = DateTime.UtcNow });
    }

    private static async Task<IResult> DisconnectClient(
        IScalableNotificationService notificationService,
        string connectionId
    )
    {
        await notificationService.DisconnectAsync(connectionId);
        return Results.Ok(
            new { Message = "Connection disconnected successfully", ConnectionId = connectionId }
        );
    }

    private static async Task<IResult> SendToConnection(
        IScalableNotificationService notificationService,
        string connectionId,
        MessageRequest request
    )
    {
        await notificationService.PublishToConnectionAsync(connectionId, request.Message);
        return Results.Ok(
            new { Message = "Message sent successfully", ConnectionId = connectionId }
        );
    }

    private static async Task<IResult> BroadcastToAll(
        IScalableNotificationService notificationService,
        MessageRequest request
    )
    {
        await notificationService.PublishToAllAsync(request.Message);
        return Results.Ok(new { Message = "Message broadcast to all connections successfully" });
    }

    private static async Task<IResult> GetStats(IScalableNotificationService notificationService)
    {
        var stats = await notificationService.GetStatsAsync();
        return Results.Ok(stats);
    }

    private static async Task<IResult> GetHealth(
        IScalableNotificationService notificationService,
        IGroupNotificationService groupService
    )
    {
        try
        {
            var stats = await notificationService.GetStatsAsync();
            var groupStats = await groupService.GetGroupStatisticsAsync();

            var health = new
            {
                Status = "Healthy",
                Timestamp = DateTime.UtcNow,
                ActiveConnections = stats.ActiveConnections,
                TotalGroups = groupStats.Count,
                MessagesPerSecond = stats.MessagesPerSecond,
            };

            return Results.Ok(health);
        }
        catch (Exception ex)
        {
            var health = new
            {
                Status = "Unhealthy",
                Timestamp = DateTime.UtcNow,
                ActiveConnections = 0,
                TotalGroups = 0,
                MessagesPerSecond = 0L,
                Error = ex.Message,
            };

            return Results.Json(health, statusCode: 503);
        }
    }

    /// <summary>
    /// Configure SSE headers that are compatible with HTTP/1.1, HTTP/2, and HTTP/3
    /// </summary>
    private static void ConfigureSseHeaders(HttpContext context)
    {
        context.Response.Headers.Append("Content-Type", "text/event-stream");
        context.Response.Headers.Append("Cache-Control", "no-cache, no-store, must-revalidate");
        context.Response.Headers.Append("Access-Control-Allow-Origin", "*");

        // Only set Connection header for HTTP/1.1 (not valid for HTTP/2 and HTTP/3)
        if (context.Request.Protocol == "HTTP/1.1")
        {
            context.Response.Headers.Append("Connection", "keep-alive");
        }

        // Set additional headers for better SSE support and performance
        context.Response.Headers.Append("X-Accel-Buffering", "no"); // Disable nginx buffering
        context.Response.Headers.Append("Pragma", "no-cache"); // HTTP/1.0 compatibility
        context.Response.Headers.Append("Expires", "0"); // Prevent caching

        // CORS headers for better browser compatibility
        context.Response.Headers.Append("Access-Control-Allow-Headers", "Cache-Control");
        context.Response.Headers.Append("Access-Control-Allow-Methods", "GET, OPTIONS");
    }
}
