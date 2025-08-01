using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace SSEChannel.Core.HealthChecks;

/// <summary>
/// Custom health check for SSE Channel services
/// </summary>
public class SSEChannelHealthCheck(
    IScalableNotificationService notificationService,
    IGroupNotificationService groupService,
    ICacheService cacheService,
    ILogger<SSEChannelHealthCheck> logger
    ) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            var healthData = new Dictionary<string, object>();

            // Check notification service
            var stats = await notificationService.GetStatsAsync();
            healthData["activeConnections"] = stats.ActiveConnections;
            healthData["totalConnections"] = stats.TotalConnections;
            healthData["messagesPerSecond"] = stats.MessagesPerSecond;

            // Check group service
            var groupStats = await groupService.GetGroupStatisticsAsync();
            healthData["totalGroups"] = groupStats.Count;
            healthData["totalGroupMembers"] = groupStats.Values.Sum();

            // Check cache service
            try
            {
                var cachedStats = await cacheService.GetConnectionStatsAsync();
                healthData["cacheWorking"] = true;
                healthData["cacheHasStats"] = cachedStats != null;
            }
            catch
            {
                healthData["cacheWorking"] = false;
            }

            // Determine health status
            var isHealthy = stats.ActiveConnections >= 0; // Basic sanity check
            var status = isHealthy ? HealthStatus.Healthy : HealthStatus.Degraded;

            // Add warnings for high load
            if (stats.ActiveConnections > 100000)
            {
                healthData["warning"] = "High connection count detected";
                status = HealthStatus.Degraded;
            }

            return new HealthCheckResult(
                status,
                description: $"SSE Channel is {(isHealthy ? "healthy" : "degraded")}",
                data: healthData
            );
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Health check failed");
            return new HealthCheckResult(
                HealthStatus.Unhealthy,
                description: "SSE Channel health check failed",
                exception: ex
            );
        }
    }
}
