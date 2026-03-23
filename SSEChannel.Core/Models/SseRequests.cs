namespace SSEChannel.Core.Models;

/// <summary>Request body for publishing a server-side message to a channel.</summary>
public record PublishRequest(string Message, string? EventName = null);

/// <summary>Request body for sending a client-originated message to a channel.</summary>
public record SendRequest(string Message, string? EventName = null);
