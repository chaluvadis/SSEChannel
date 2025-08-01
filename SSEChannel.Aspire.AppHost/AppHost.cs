using Microsoft.Extensions.Hosting;
using Projects;

var builder = DistributedApplication.CreateBuilder(args);

// Add the main SSE Channel application with enhanced configuration
var sseChannel = builder
    .AddProject<SSEChannel_Core>("ssechannel")
    .WithReplicas(1) // Can be scaled up for load testing
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", builder.Environment.EnvironmentName)
    .WithEnvironment("Notifications:PartitionCount", "8") // Optimize for performance
    .WithEnvironment("Notifications:CleanupIntervalMs", "30000")
    .WithEnvironment("Notifications:EnableAutoBroadcast", "true")
    .WithEnvironment("Notifications:BroadcastIntervalMs", "2000");

// Configure the SSE Channel for different environments
if (builder.Environment.EnvironmentName == Environments.Development)
{
    // Development configuration
    sseChannel
        .WithEnvironment("Logging:LogLevel:Default", "Information")
        .WithEnvironment("Logging:LogLevel:SSEChannel", "Debug");
}
else
{
    // Production configuration
    sseChannel
        .WithEnvironment("Logging:LogLevel:Default", "Warning")
        .WithEnvironment("Logging:LogLevel:SSEChannel", "Information")
        .WithReplicas(3); // Scale up for production
}

// Add Grafana for metrics visualization (optional)
if (builder.Configuration["EnableGrafana"] == "true")
{
    builder
        .AddContainer("grafana", "grafana/grafana")
        .WithBindMount("./grafana/dashboards", "/etc/grafana/provisioning/dashboards")
        .WithBindMount("./grafana/datasources", "/etc/grafana/provisioning/datasources")
        .WithEnvironment("GF_SECURITY_ADMIN_PASSWORD", "admin")
        .WithHttpEndpoint(port: 3000, targetPort: 3000, name: "grafana");
}

builder.Build().Run();
