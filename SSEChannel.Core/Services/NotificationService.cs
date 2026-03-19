using System.Collections.Concurrent;

namespace SSEChannel.Core.Services;

public class NotificationService : INotificationService
{
    // Using collection expressions and improved concurrent collections
    private readonly ConcurrentDictionary<Guid, Channel<string>> _clients = new();

    public async ValueTask PublishAsync(Guid id, string message)
    {
        if (_clients.TryGetValue(id, out var channel))
        {
            try
            {
                await channel.Writer.WriteAsync(message);
            }
            catch (InvalidOperationException)
            {
                // Channel was closed, remove it
                _clients.TryRemove(id, out _);
            }
        }
    }

    public ChannelReader<string> Subscribe(Guid id)
    {
        var channel = _clients.GetOrAdd(id, _ => Channel.CreateUnbounded<string>());
        return channel.Reader;
    }

    public Guid RegisterClient(Guid id)
    {
        var channel = _clients.GetOrAdd(id, _ => Channel.CreateUnbounded<string>());

        // Send a welcome message to the newly connected client
        _ = Task.Run(async () =>
        {
            try
            {
                await channel.Writer.WriteAsync(
                    $"Welcome! Client {id} connected at {DateTime.Now:HH:mm:ss}"
                );
            }
            catch (InvalidOperationException)
            {
                // Channel was closed
            }
        });

        return id;
    }

    // Using collection expressions
    public IList<Guid> GetChannelds() => _clients.Keys.ToList();
}
