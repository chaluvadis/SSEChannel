namespace SSEChannel.Core.Services.Interfaces;

public interface IMetricsService
{
    public void RecordConnection(string connectionType = "sse");

    public void RecordMessage(string messageType = "broadcast", int recipientCount = 1);

    public void RecordGroupOperation(string operation, string? groupName = null);
    public void RecordMessageLatency(double latencyMs, string messageType = "broadcast");
}
