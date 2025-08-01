namespace SSEChannel.Core.Routes;

public static class NotificationRoutes
{
    public static void MapNotificationRoutes(this WebApplication app)
    {
        var group = app.MapGroup("/sse")
            .WithTags("SimpleNotifications")
            .WithSummary("Simple Notification API");

        // SSE endpoint with client ID parameter - showcasing minimal API improvements
        group.MapGet("/sse/notifications/{clientId:guid?}", GetNotification);

        // Receive message endpoint - using .NET 10 extension types
        group.MapPost("/sse/receive", ReceiveMessage);

        // Get all connected clients - showcasing collection expressions
        group.MapGet("/sse/clients", GetAllClients);
    }

    private static IResult GetAllClients(INotificationService notifications)
    {
        var clients = notifications.GetChannelds();
        return Results.Ok(new { ClientCount = clients.Count, Clients = clients });
    }

    private static async Task<IResult> ReceiveMessage(
        INotificationService notifications,
        IncomingMessage message
    )
    {
        await notifications.PublishAsync(message.Id, message.Message);
        return Results.Ok(message.ToResponse());
    }

    private static async Task GetNotification(
        HttpContext context,
        INotificationService notifications,
        Guid? clientId
    )
    {
        // Configure SSE headers for optimal performance (HTTP/2 and HTTP/3 compatible)
        ConfigureSseHeaders(context);

        var actualClientId = clientId ?? Guid.NewGuid();

        // Register client and get the channel reader
        notifications.RegisterClient(actualClientId);
        var reader = notifications.Subscribe(actualClientId);

        // Send initial connection confirmation
        await context.Response.WriteAsync($"data: Connected as client {actualClientId}\n\n");
        await context.Response.Body.FlushAsync();

        try
        {
            while (await reader.WaitToReadAsync(context.RequestAborted))
            {
                while (reader.TryRead(out var notification))
                {
                    await context.Response.WriteAsync($"data: {notification}\n\n");
                    await context.Response.Body.FlushAsync();
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Client disconnected - this is normal
        }
        catch (Exception ex)
        {
            // Log other exceptions
            Console.WriteLine($"SSE Error for client {actualClientId}: {ex.Message}");
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
