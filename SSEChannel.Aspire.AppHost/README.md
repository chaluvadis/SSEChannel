# SSEChannel Aspire Integration

This project provides .NET Aspire orchestration for the SSEChannel application, enabling cloud-native development with built-in observability, service discovery, and resilience.

## Features

### 🚀 **Service Orchestration**

- **SSEChannel.Core**: Main SSE application with scalable architecture
- **Redis**: Distributed caching and session state management
- **PostgreSQL**: Database for future data persistence needs
- **Redis Commander**: Web UI for Redis management
- **pgAdmin**: Web UI for PostgreSQL management

### 📊 **Observability**

- **OpenTelemetry**: Distributed tracing and metrics collection
- **Custom Metrics**: SSE-specific metrics (connections, messages, latency)
- **Health Checks**: Comprehensive health monitoring
- **Structured Logging**: Enhanced logging with correlation IDs

### 🔧 **Development Features**

- **Service Discovery**: Automatic service resolution
- **Resilience**: Built-in retry policies and circuit breakers
- **Hot Reload**: Fast development iteration
- **Environment Configuration**: Different settings per environment

## Getting Started

### Prerequisites

- .NET 10.0 SDK or later
- Docker Desktop (for containerized services)
- Visual Studio 2024 or VS Code with C# extension

### Running the Application

1. **Start the Aspire AppHost**:

   ```bash
   cd SSEChannel.Aspire.AppHost
   dotnet run
   ```

2. **Access the Aspire Dashboard**:

   - Open your browser to `https://localhost:17000` (or the URL shown in console)
   - View real-time metrics, logs, and traces

3. **Access the SSE Application**:

   - Main app: `https://localhost:7000` (or port shown in dashboard)
   - Scalability test: `https://localhost:7000/scalability`
   - Group management: `https://localhost:7000/group`

4. **Access Management UIs**:
   - Redis Commander: Available through Aspire dashboard
   - pgAdmin: Available through Aspire dashboard

### Configuration

#### Environment Variables

The AppHost configures the following environment variables for optimal performance:

```json
{
  "Notifications:PartitionCount": "8",
  "Notifications:CleanupIntervalMs": "30000",
  "Notifications:EnableAutoBroadcast": "true",
  "Notifications:BroadcastIntervalMs": "2000"
}
```

#### Scaling

- **Development**: 1 replica by default
- **Production**: 3 replicas with load balancing
- **Manual scaling**: Modify `WithReplicas()` in AppHost.cs

### Monitoring and Observability

#### Custom Metrics

The application exposes the following custom metrics:

- `ssechannel_connections_total`: Total connections created
- `ssechannel_messages_total`: Total messages sent
- `ssechannel_group_operations_total`: Total group operations
- `ssechannel_message_latency_ms`: Message delivery latency
- `ssechannel_active_connections`: Current active connections
- `ssechannel_active_groups`: Current active groups

#### Health Checks

- **Liveness**: Basic application responsiveness
- **Readiness**: Full service health including dependencies
- **Custom**: SSE-specific health metrics

#### Distributed Tracing

All HTTP requests, Redis operations, and database queries are automatically traced with OpenTelemetry.

### Architecture

```
┌─────────────────┐    ┌─────────────────┐    ┌─────────────────┐
│   SSEChannel    │    │      Redis      │    │   PostgreSQL    │
│      Core       │◄──►│     Cache       │    │    Database     │
│                 │    │                 │    │                 │
└─────────────────┘    └─────────────────┘    └─────────────────┘
         │                       │                       │
         └───────────────────────┼───────────────────────┘
                                 │
                    ┌─────────────────┐
                    │     Aspire      │
                    │   Dashboard     │
                    │  (Observability)│
                    └─────────────────┘
```

### Performance Optimization

#### Redis Caching Strategy

- **Connection Stats**: 30-second TTL
- **Group Statistics**: 60-second TTL
- **Group Member Counts**: 30-second TTL

#### Connection Partitioning

- Default: 8 partitions (configurable)
- Distributes connections across CPU cores
- Enables parallel message processing

#### Kestrel Configuration

- Max concurrent connections: 1,000,000
- Keep-alive timeout: 2 minutes
- Request timeout: 30 seconds

### Troubleshooting

#### Common Issues

1. **Port Conflicts**:

   - Check Aspire dashboard for actual port assignments
   - Ports are dynamically allocated by default

2. **Redis Connection Issues**:

   - Ensure Docker Desktop is running
   - Check Redis container status in Aspire dashboard

3. **High Memory Usage**:

   - Monitor connection counts in health checks
   - Adjust partition count based on load

4. **Performance Issues**:
   - Check custom metrics in Aspire dashboard
   - Monitor message latency and throughput
   - Scale replicas if needed

#### Debugging

1. **Enable Debug Logging**:

   ```json
   {
     "Logging": {
       "LogLevel": {
         "SSEChannel": "Debug"
       }
     }
   }
   ```

2. **View Distributed Traces**:

   - Use Aspire dashboard trace viewer
   - Filter by service name or operation

3. **Monitor Custom Metrics**:
   - Check metrics tab in Aspire dashboard
   - Set up alerts for threshold breaches

### Production Deployment

#### Scaling Configuration

```csharp
// In AppHost.cs for production
sseChannel.WithReplicas(3)
          .WithEnvironment("Notifications:PartitionCount", "16")
          .WithEnvironment("Logging:LogLevel:Default", "Warning");
```

#### Resource Limits

- Configure appropriate CPU and memory limits
- Monitor resource usage through Aspire dashboard
- Set up auto-scaling based on connection metrics

### Contributing

When adding new features:

1. **Add Custom Metrics**: Use `MetricsService` for new measurements
2. **Update Health Checks**: Include new dependencies in health checks
3. **Configure Tracing**: Ensure new operations are traced
4. **Update Documentation**: Keep this README current

### Related Documentation

- [.NET Aspire Documentation](https://learn.microsoft.com/en-us/dotnet/aspire/)
- [OpenTelemetry .NET](https://opentelemetry.io/docs/languages/net/)
- [Redis Documentation](https://redis.io/documentation)
- [PostgreSQL Documentation](https://www.postgresql.org/docs/)
