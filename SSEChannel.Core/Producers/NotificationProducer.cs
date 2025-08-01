namespace SSEChannel.Core.Producers;

public class NotificationProducer(INotificationService notificationService) : BackgroundService
{
    private readonly INotificationService _notificationService = notificationService;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        int counter = 1;

        // Using improved async enumerable and periodic timer
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            var channelIds = _notificationService.GetChannelds();

            // Send notifications to all connected clients
            if (channelIds.Count > 0)
            {
                var timestamp = DateTime.Now.ToString("HH:mm:ss");
                var tasks = new List<Task>();

                foreach (var id in channelIds)
                {
                    var notification = $"Broadcast #{counter} to {id} at {timestamp}";
                    tasks.Add(_notificationService.PublishAsync(id, notification).AsTask());
                }

                await Task.WhenAll(tasks);
                counter++;
            }
            else
            {
                // No clients connected, but keep the counter running
                counter++;
            }
        }
    }
}
