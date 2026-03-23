# SSE Channel

A high-performance Server-Sent Events (SSE) notification system built with .NET.

## Quick Start

```bash
dotnet run
```

Open `http://localhost:5000` in your browser.

## Features

- **SSE streaming** - Real-time message delivery to connected clients
- **Connection partitioning** - Scales to millions of concurrent connections
- **Group messaging** - Send messages to specific groups
- **Broadcast** - Send messages to all connected clients
- **Health monitoring** - Real-time stats at `/health`
- **Auto cleanup** - Automatic disconnection handling

## API Endpoints

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/stream/{connectionId?}` | GET | SSE stream |
| `/api/v2/notifications/connect` | POST | Connect |
| `/api/v2/notifications/{connectionId}` | DELETE | Disconnect |
| `/api/v2/notifications/send/{connectionId}` | POST | Send to client |
| `/api/v2/notifications/broadcast/all` | POST | Broadcast all |
| `/api/v2/notifications/groups/{groupName}/join` | POST | Join group |
| `/api/v2/notifications/groups/{groupName}/leave` | POST | Leave group |
| `/api/v2/notifications/groups/{groupName}/broadcast` | POST | Broadcast group |
| `/api/v2/notifications/stats` | GET | Statistics |

## Configuration

Edit `SSEChannel.Core/appsettings.json`:

```json
{
  "Notifications": {
    "PartitionCount": 16,
    "CleanupIntervalMs": 30000
  }
}
```

## Architecture

```
Services/
├── ScalableNotificationService.cs     # Main service with partitioning
├── ConnectionPartitionService.cs     # Per-partition handling
├── GroupNotificationService.cs        # Group management
├── MetricsService.cs                  # Statistics
└── InMemoryCacheService.cs            # Caching
```