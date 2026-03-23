using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using SSEChannel.Core.HealthChecks;

var builder = WebApplication.CreateBuilder(args);

// Add Aspire service defaults (observability, service discovery, resilience)
builder.AddServiceDefaults();

// Add in-memory caching
builder.Services.AddMemoryCache();

// Register cache service (in-memory implementation)
builder.Services.AddSingleton<ICacheService, InMemoryCacheService>();

// Register metrics service
builder.Services.AddSingleton<IMetricsService ,MetricsService>();

// Register connection event service for coordinating cleanup
builder.Services.AddSingleton<IConnectionEventService, ConnectionEventService>();

// Configure Kestrel for high performance
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxConcurrentConnections = 1_000_000;
    options.Limits.MaxConcurrentUpgradedConnections = 1_000_000;
    options.Limits.MaxRequestBodySize = 1_048_576; // 1MB
    options.Limits.KeepAliveTimeout = TimeSpan.FromMinutes(2);
    options.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(30);
});

// Add services
builder.Services.AddControllers();

// Scalable services (primary implementation)
builder.Services.AddSingleton<IScalableNotificationService, ScalableNotificationService>();

// Group service that depends on notification service
builder.Services.AddSingleton<IGroupNotificationService>(provider =>
{
    var notificationService = provider.GetRequiredService<IScalableNotificationService>();
    var logger = provider.GetRequiredService<ILogger<GroupNotificationService>>();
    var configuration = provider.GetRequiredService<IConfiguration>();
    return new GroupNotificationService(
        (connectionId, message) =>
            notificationService.PublishToConnectionAsync(connectionId, message),
        logger,
        configuration
    );
});

builder.Services.AddHostedService<ScalableNotificationProducer>();

// Legacy services (for backward compatibility)
builder.Services.AddSingleton<INotificationService, NotificationService>();
builder.Services.AddHostedService<NotificationProducer>();

// Performance optimizations
builder.Services.AddOutputCache(options =>
{
    options.AddBasePolicy(builder => builder.Cache());
});

// Add services to the container.
builder.Services.AddRazorPages();

// New SSE Channel system
builder.Services.AddSseChannels(options =>
{
    builder.Configuration.GetSection(SseOptions.SectionName).Bind(options);
});

// Add custom health checks
builder
    .Services.AddHealthChecks()
    .AddCheck<SSEChannelHealthCheck>("ssechannel", tags: ["ready", "live"]);

// Add logging
builder.Logging.AddConsole();
builder.Logging.SetMinimumLevel(LogLevel.Information);

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();

// Add output caching
app.UseOutputCache();

// Map routes - scalable routes (primary)
app.MapScalableNotificationRoutes(); // Scalable routes
app.MapGroupNotificationRoutes(); // Dedicated group management routes
app.MapNotificationRoutes(); // Legacy routes for backward compatibility
app.MapSseChannels(); // New SSE Channel system

app.UseAuthorization();

app.MapStaticAssets();
app.MapControllers();
app.MapRazorPages().WithStaticAssets();

// Map Aspire default endpoints (health checks, etc.)
app.MapDefaultEndpoints();

app.Run();
