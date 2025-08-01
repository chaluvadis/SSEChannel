namespace SSEChannel.Core.Services.Interfaces;

public interface IConnectionPartitionService
{
    public void AddConnection(string connectionId);
    public void RemoveConnection(string connectionId);
    public ValueTask PublishToConnectionAsync(string connectionId, string message);
    public ValueTask PublishToAllAsync(string message);
    public ChannelReader<string> Subscribe(string connectionId);
    public void CleanupDisconnectedClients();
    public IEnumerable<string> GetConnectionIds();
}
