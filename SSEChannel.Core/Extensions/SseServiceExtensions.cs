using SSEChannel.Core.Backplane;
using SSEChannel.Core.Channels;
using SSEChannel.Core.Connections;
using SSEChannel.Core.Models;
using StackExchange.Redis;

namespace SSEChannel.Core.Extensions;

public static class SseServiceExtensions
{
    /// <summary>Register ISseChannel services. Options can be customised via configure parameter.</summary>
    public static IServiceCollection AddSseChannels(
        this IServiceCollection services,
        Action<SseOptions>? configure = null)
    {
        var options = new SseOptions();
        configure?.Invoke(options);

        services.AddSingleton(options);
        services.AddSingleton<SseClientStore>(sp =>
            new SseClientStore(sp.GetRequiredService<SseOptions>().ReplayBufferSize));

        if (options.UseRedisBackplane && !string.IsNullOrWhiteSpace(options.RedisConnectionString))
        {
            services.AddSingleton<IConnectionMultiplexer>(_ =>
                ConnectionMultiplexer.Connect(options.RedisConnectionString));
            services.AddSingleton<IChannelBackplane, RedisChannelBackplane>();
        }
        else
        {
            services.AddSingleton<IChannelBackplane, InMemoryChannelBackplane>();
        }

        services.AddSingleton<SseChannelService>();
        services.AddSingleton<ISseChannel>(sp => sp.GetRequiredService<SseChannelService>());
        services.AddHostedService(sp => sp.GetRequiredService<SseChannelService>());

        return services;
    }

    /// <summary>Map SSE subscribe, publish, send, stats, and channel-list endpoints under /sse/.</summary>
    public static IEndpointRouteBuilder MapSseChannels(this IEndpointRouteBuilder app)
    {
        app.MapGet("/sse/{channel}", async (
            string channel,
            ISseChannel sseChannel,
            HttpContext context) =>
        {
            await sseChannel.SubscribeAsync(channel, context);
        })
        .WithName("SseSubscribe")
        .WithTags("SseChannel")
        .WithSummary("Subscribe to a channel via Server-Sent Events");

        app.MapPost("/sse/{channel}/publish", async (
            string channel,
            ISseChannel sseChannel,
            PublishRequest request,
            ILogger<SseChannelService> logger) =>
        {
            if (string.IsNullOrWhiteSpace(request.Message))
                return Results.BadRequest(new { error = "Message must not be empty." });

            try
            {
                await sseChannel.PublishAsync(channel, request, request.EventName ?? "message");
                return Results.Ok(new
                {
                    channel,
                    eventName = request.EventName ?? "message",
                    clientCount = sseChannel.GetClientCount(channel),
                    timestamp = DateTimeOffset.UtcNow,
                });
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to publish to channel {Channel}", channel);
                return Results.Problem("Failed to publish message.");
            }
        })
        .WithName("SsePublish")
        .WithTags("SseChannel")
        .WithSummary("Publish a message to a channel");

        app.MapPost("/sse/{channel}/send", async (
            string channel,
            ISseChannel sseChannel,
            SendRequest request,
            ILogger<SseChannelService> logger) =>
        {
            if (string.IsNullOrWhiteSpace(request.Message))
                return Results.BadRequest(new { error = "Message must not be empty." });

            try
            {
                await sseChannel.SendFromClientAsync(channel, request, request.EventName ?? "client-message");
                return Results.Ok(new
                {
                    channel,
                    eventName = request.EventName ?? "client-message",
                    timestamp = DateTimeOffset.UtcNow,
                });
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to send client message to channel {Channel}", channel);
                return Results.Problem("Failed to send message.");
            }
        })
        .WithName("SseSend")
        .WithTags("SseChannel")
        .WithSummary("Send a message from a client to a channel (broadcasts to all subscribers)");

        app.MapGet("/sse/{channel}/stats", (string channel, ISseChannel sseChannel) =>
            Results.Ok(new
            {
                channel,
                connectedClients = sseChannel.GetClientCount(channel),
                timestamp = DateTimeOffset.UtcNow,
            }))
        .WithName("SseChannelStats")
        .WithTags("SseChannel")
        .WithSummary("Get connected client count for a channel");

        app.MapGet("/sse/channels", (ISseChannel sseChannel) =>
        {
            var channels = sseChannel.GetChannels();
            return Results.Ok(new
            {
                channels,
                total = channels.Count,
                timestamp = DateTimeOffset.UtcNow,
            });
        })
        .WithName("SseAllChannels")
        .WithTags("SseChannel")
        .WithSummary("List all active SSE channels");

        return app;
    }
}
