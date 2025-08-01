namespace SSEChannel.Core.Services.Interfaces;

public interface INotificationService
{
    ValueTask PublishAsync(Guid id, string message);
    ChannelReader<string> Subscribe(Guid id);
    Guid RegisterClient(Guid id);
    IList<Guid> GetChannelds();
}
