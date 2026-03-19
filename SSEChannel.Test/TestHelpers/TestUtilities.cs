using System.Threading.Channels;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SSEChannel.Core.Services;
using SSEChannel.Core.Services.Interfaces;

namespace SSEChannel.Test.TestHelpers;

/// <summary>
/// Utility class for common test operations and setup
/// </summary>
public static class TestUtilities
{
    /// <summary>
    /// Creates a configured ScalableNotificationService for testing
    /// </summary>
    public static IScalableNotificationService CreateNotificationService()
    {
        var loggerFactory = new LoggerFactory();
        var logger = loggerFactory.CreateLogger<ScalableNotificationService>();

        // Create test configuration
        var configData = new Dictionary<string, string?>
        {
            ["Notifications:PartitionCount"] = "4", // Use fewer partitions for testing
            ["Notifications:CleanupIntervalMs"] = "5000", // Shorter cleanup interval for tests
        };

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(configData).Build();

        // Create a mock connection event service for testing
        var mockConnectionEventService = new MockConnectionEventService();

        return new ScalableNotificationService(
            configuration,
            logger,
            loggerFactory,
            mockConnectionEventService
        );
    }

    /// <summary>
    /// Creates a configured GroupNotificationService for testing
    /// </summary>
    public static IGroupNotificationService CreateGroupService(
        Func<string, string, ValueTask>? publishFunction = null
    )
    {
        var logger = new LoggerFactory().CreateLogger<GroupNotificationService>();

        publishFunction ??= (connectionId, message) => ValueTask.CompletedTask;

        return new GroupNotificationService(publishFunction, logger);
    }

    /// <summary>
    /// Creates both services configured to work together
    /// </summary>
    public static (
        IScalableNotificationService notification,
        IGroupNotificationService group
    ) CreateIntegratedServices()
    {
        var loggerFactory = new LoggerFactory();
        var notificationLogger = loggerFactory.CreateLogger<ScalableNotificationService>();
        var groupLogger = loggerFactory.CreateLogger<GroupNotificationService>();

        // Create test configuration
        var configData = new Dictionary<string, string?>
        {
            ["Notifications:PartitionCount"] = "4", // Use fewer partitions for testing
            ["Notifications:CleanupIntervalMs"] = "5000", // Shorter cleanup interval for tests
        };

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(configData).Build();

        // First create a temporary group service for the notification service
        var tempGroupService = new GroupNotificationService(
            (connectionId, message) => ValueTask.CompletedTask,
            groupLogger
        );

        // Create the notification service
        var mockConnectionEventService = new MockConnectionEventService();
        var notificationService = new ScalableNotificationService(
            configuration,
            notificationLogger,
            loggerFactory,
            mockConnectionEventService
        );

        // Now create the real group service with the notification service's publish method
        var groupService = new GroupNotificationService(
            notificationService.PublishToConnectionAsync,
            groupLogger
        );

        return (notificationService, groupService);
    }

    /// <summary>
    /// Waits for a message with timeout and returns it
    /// </summary>
    public static async Task<string> WaitForMessageAsync(
        ChannelReader<string> reader,
        TimeSpan? timeout = null
    )
    {
        timeout ??= TimeSpan.FromSeconds(5);
        var cts = new CancellationTokenSource(timeout.Value);
        return await reader.ReadAsync(cts.Token);
    }

    /// <summary>
    /// Waits for multiple messages with timeout
    /// </summary>
    public static async Task<List<string>> WaitForMessagesAsync(
        ChannelReader<string> reader,
        int count,
        TimeSpan? timeout = null
    )
    {
        timeout ??= TimeSpan.FromSeconds(10);
        var cts = new CancellationTokenSource(timeout.Value);
        var messages = new List<string>();

        for (int i = 0; i < count; i++)
        {
            var message = await reader.ReadAsync(cts.Token);
            messages.Add(message);
        }

        return messages;
    }

    /// <summary>
    /// Creates multiple connections and returns their IDs and readers
    /// </summary>
    public static async Task<(
        List<string> connectionIds,
        List<ChannelReader<string>> readers
    )> CreateMultipleConnectionsAsync(
        IScalableNotificationService service,
        int count,
        string prefix = "test-conn"
    )
    {
        var connectionIds = new List<string>();
        var readers = new List<ChannelReader<string>>();

        for (int i = 0; i < count; i++)
        {
            var connectionId = await service.ConnectAsync($"{prefix}-{i}");
            var reader = service.Subscribe(connectionId);

            connectionIds.Add(connectionId);
            readers.Add(reader);
        }

        return (connectionIds, readers);
    }

    /// <summary>
    /// Verifies that all readers receive the expected message
    /// </summary>
    public static async Task VerifyAllReceiveMessageAsync(
        List<ChannelReader<string>> readers,
        string expectedMessage,
        TimeSpan? timeout = null
    )
    {
        var tasks = readers.Select(reader => WaitForMessageAsync(reader, timeout));
        var messages = await Task.WhenAll(tasks);

        foreach (var message in messages)
        {
            Assert.AreEqual(expectedMessage, message);
        }
    }

    /// <summary>
    /// Creates a group with specified members
    /// </summary>
    public static async Task CreateGroupWithMembersAsync(
        IGroupNotificationService groupService,
        string groupName,
        IEnumerable<string> connectionIds
    )
    {
        var joinTasks = connectionIds.Select(conn =>
            groupService.JoinGroupAsync(conn, groupName).AsTask()
        );
        await Task.WhenAll(joinTasks);
    }

    /// <summary>
    /// Generates test data for performance tests
    /// </summary>
    public static class TestData
    {
        public static IEnumerable<string> GenerateConnectionIds(
            int count,
            string prefix = "conn"
        ) => Enumerable.Range(1, count).Select(i => $"{prefix}-{i}");

        public static IEnumerable<string> GenerateGroupNames(int count, string prefix = "group") =>
            Enumerable.Range(1, count).Select(i => $"{prefix}-{i}");

        public static IEnumerable<string> GenerateMessages(int count, string prefix = "message") =>
            Enumerable.Range(1, count).Select(i => $"{prefix}-{i}");
    }

    /// <summary>
    /// Performance measurement utilities
    /// </summary>
    public static class Performance
    {
        public static async Task<TimeSpan> MeasureAsync(Func<Task> operation)
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            await operation();
            stopwatch.Stop();
            return stopwatch.Elapsed;
        }

        public static async Task<(T result, TimeSpan elapsed)> MeasureAsync<T>(
            Func<Task<T>> operation
        )
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var result = await operation();
            stopwatch.Stop();
            return (result, stopwatch.Elapsed);
        }

        public static void AssertPerformance(
            TimeSpan elapsed,
            TimeSpan maxExpected,
            string operation
        )
        {
            Assert.IsTrue(
                elapsed <= maxExpected,
                $"{operation} took {elapsed.TotalMilliseconds:F2}ms, expected <= {maxExpected.TotalMilliseconds:F2}ms"
            );
        }

        public static void AssertThroughput(
            int operations,
            TimeSpan elapsed,
            double minOperationsPerSecond,
            string operation
        )
        {
            var actualRate = operations / elapsed.TotalSeconds;
            Assert.IsTrue(
                actualRate >= minOperationsPerSecond,
                $"{operation} rate was {actualRate:F0}/sec, expected >= {minOperationsPerSecond:F0}/sec"
            );
        }
    }
}

/// <summary>
/// Mock implementation for testing connection events
/// </summary>
public class MockConnectionEventService : IConnectionEventService
{
    public ValueTask OnConnectionDisconnectedAsync(string connectionId)
    {
        // Mock implementation - do nothing
        return ValueTask.CompletedTask;
    }

    public ValueTask OnConnectionsCleanupAsync(IEnumerable<string> activeConnectionIds)
    {
        // Mock implementation - do nothing
        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// Mock implementation for testing group service without notification service dependency
/// </summary>
public class MockPublishFunction
{
    private readonly Dictionary<string, List<string>> _connectionMessages = new();
    private readonly List<string> _allMessages = new();

    public ValueTask PublishAsync(string connectionId, string message)
    {
        if (!_connectionMessages.ContainsKey(connectionId))
            _connectionMessages[connectionId] = new List<string>();

        _connectionMessages[connectionId].Add(message);
        _allMessages.Add(message);

        return ValueTask.CompletedTask;
    }

    public List<string> GetMessagesForConnection(string connectionId)
    {
        return _connectionMessages.GetValueOrDefault(connectionId, new List<string>());
    }

    public List<string> GetAllMessages() => new(_allMessages);

    public void Clear()
    {
        _connectionMessages.Clear();
        _allMessages.Clear();
    }

    public int GetTotalMessageCount() => _allMessages.Count;

    public int GetConnectionCount() => _connectionMessages.Count;
}
