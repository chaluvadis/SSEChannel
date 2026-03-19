using System.Diagnostics;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using SSEChannel.Core.Services;
using SSEChannel.Core.Services.Interfaces;
using SSEChannel.Test.TestHelpers;

namespace SSEChannel.Test.Performance;

[TestClass]
public class ScalabilityTests
{
    private IScalableNotificationService _notificationService = null!;
    private IGroupNotificationService _groupService = null!;
    private ILogger<ScalableNotificationService> _notificationLogger = null!;
    private ILogger<GroupNotificationService> _groupLogger = null!;

    [TestInitialize]
    public void Setup()
    {
        var loggerFactory = new LoggerFactory();
        _notificationLogger = loggerFactory.CreateLogger<ScalableNotificationService>();
        _groupLogger = loggerFactory.CreateLogger<GroupNotificationService>();

        var (notificationService, groupService) = TestUtilities.CreateIntegratedServices();
        _notificationService = notificationService;
        _groupService = groupService;
    }

    [TestCleanup]
    public void Cleanup()
    {
        (_notificationService as IDisposable)?.Dispose();
    }

    [TestMethod]
    [TestCategory("Performance")]
    public async Task ConnectionCreation_ShouldHandleHighVolume()
    {
        // Arrange
        var connectionCount = 1000;
        var stopwatch = Stopwatch.StartNew();

        // Act
        var connectionTasks = new List<Task<string>>();
        for (int i = 0; i < connectionCount; i++)
        {
            connectionTasks.Add(_notificationService.ConnectAsync($"perf-conn-{i}").AsTask());
        }

        var connections = await Task.WhenAll(connectionTasks);
        stopwatch.Stop();

        // Assert
        Assert.AreEqual(connectionCount, connections.Length);
        Assert.AreEqual(connectionCount, connections.Distinct().Count());

        var connectionsPerSecond = connectionCount / stopwatch.Elapsed.TotalSeconds;
        Console.WriteLine(
            $"Created {connectionCount} connections in {stopwatch.ElapsedMilliseconds}ms"
        );
        Console.WriteLine($"Rate: {connectionsPerSecond:F0} connections/second");

        // Should be able to create at least 100 connections per second
        Assert.IsTrue(
            connectionsPerSecond > 100,
            $"Connection creation rate too slow: {connectionsPerSecond:F0}/sec"
        );
    }

    [TestMethod]
    [TestCategory("Performance")]
    public async Task MessageThroughput_ShouldHandleHighVolume()
    {
        // Arrange
        var connectionCount = 100;
        var messagesPerConnection = 10;
        var totalMessages = connectionCount * messagesPerConnection;

        var connections = new List<string>();
        var readers = new List<ChannelReader<string>>();

        for (int i = 0; i < connectionCount; i++)
        {
            var conn = await _notificationService.ConnectAsync($"throughput-conn-{i}");
            connections.Add(conn);
            readers.Add(_notificationService.Subscribe(conn));
        }

        var stopwatch = Stopwatch.StartNew();

        // Act - Send messages concurrently
        var publishTasks = new List<Task>();
        for (int i = 0; i < messagesPerConnection; i++)
        {
            var message = $"Message {i}";
            publishTasks.Add(_notificationService.PublishToAllAsync(message).AsTask());
        }

        await Task.WhenAll(publishTasks);
        stopwatch.Stop();

        // Assert - Verify throughput
        var messagesPerSecond = totalMessages / stopwatch.Elapsed.TotalSeconds;
        Console.WriteLine($"Sent {totalMessages} messages in {stopwatch.ElapsedMilliseconds}ms");
        Console.WriteLine($"Throughput: {messagesPerSecond:F0} messages/second");

        // Should handle at least 1000 messages per second
        Assert.IsTrue(
            messagesPerSecond > 1000,
            $"Message throughput too low: {messagesPerSecond:F0}/sec"
        );
    }

    [TestMethod]
    [TestCategory("Performance")]
    public async Task GroupOperations_ShouldScaleEfficiently()
    {
        // Arrange
        var groupCount = 50;
        var connectionsPerGroup = 20;
        var totalConnections = groupCount * connectionsPerGroup;

        var connections = new List<string>();
        for (int i = 0; i < totalConnections; i++)
        {
            connections.Add(await _notificationService.ConnectAsync($"group-perf-conn-{i}"));
        }

        var stopwatch = Stopwatch.StartNew();

        // Act - Create groups and add members concurrently
        var groupTasks = new List<Task>();
        for (int g = 0; g < groupCount; g++)
        {
            var groupName = $"perf-group-{g}";
            var groupConnections = connections
                .Skip(g * connectionsPerGroup)
                .Take(connectionsPerGroup);

            foreach (var conn in groupConnections)
            {
                groupTasks.Add(_groupService.JoinGroupAsync(conn, groupName).AsTask());
            }
        }

        await Task.WhenAll(groupTasks);
        stopwatch.Stop();

        // Assert
        var operationsPerSecond =
            (groupCount * connectionsPerGroup) / stopwatch.Elapsed.TotalSeconds;
        Console.WriteLine(
            $"Completed {groupCount * connectionsPerGroup} group join operations in {stopwatch.ElapsedMilliseconds}ms"
        );
        Console.WriteLine($"Rate: {operationsPerSecond:F0} operations/second");

        // Verify all groups were created correctly
        var allGroups = await _groupService.GetAllGroupsAsync();
        Assert.AreEqual(groupCount, allGroups.Count);

        // Should handle at least 500 group operations per second
        Assert.IsTrue(
            operationsPerSecond > 500,
            $"Group operation rate too slow: {operationsPerSecond:F0}/sec"
        );
    }

    [TestMethod]
    [TestCategory("Performance")]
    public async Task GroupBroadcast_ShouldHandleLargeGroups()
    {
        // Arrange
        var groupName = "large-broadcast-group";
        var memberCount = 500;
        var messageCount = 5;

        var connections = new List<string>();
        var readers = new List<ChannelReader<string>>();

        for (int i = 0; i < memberCount; i++)
        {
            var conn = await _notificationService.ConnectAsync($"broadcast-member-{i}");
            connections.Add(conn);
            readers.Add(_notificationService.Subscribe(conn));
            await _groupService.JoinGroupAsync(conn, groupName);
        }

        var stopwatch = Stopwatch.StartNew();

        // Act - Broadcast messages to the large group
        var broadcastTasks = new List<Task>();
        for (int i = 0; i < messageCount; i++)
        {
            broadcastTasks.Add(
                _groupService.PublishToGroupAsync(groupName, $"Broadcast {i}").AsTask()
            );
        }

        await Task.WhenAll(broadcastTasks);
        stopwatch.Stop();

        // Assert
        var totalDeliveries = memberCount * messageCount;
        var deliveriesPerSecond = totalDeliveries / stopwatch.Elapsed.TotalSeconds;

        Console.WriteLine(
            $"Delivered {totalDeliveries} messages to {memberCount} members in {stopwatch.ElapsedMilliseconds}ms"
        );
        Console.WriteLine($"Delivery rate: {deliveriesPerSecond:F0} deliveries/second");

        // Should handle at least 5000 message deliveries per second
        Assert.IsTrue(
            deliveriesPerSecond > 5000,
            $"Group broadcast rate too slow: {deliveriesPerSecond:F0}/sec"
        );
    }

    [TestMethod]
    [TestCategory("Performance")]
    public async Task MemoryUsage_ShouldBeReasonable()
    {
        // Arrange
        var connectionCount = 1000;
        var initialMemory = GC.GetTotalMemory(true);

        // Act - Create many connections
        var connections = new List<string>();
        for (int i = 0; i < connectionCount; i++)
        {
            connections.Add(await _notificationService.ConnectAsync($"memory-conn-{i}"));
        }

        var afterConnectionsMemory = GC.GetTotalMemory(false);
        var memoryPerConnection = (afterConnectionsMemory - initialMemory) / connectionCount;

        // Assert
        Console.WriteLine(
            $"Memory usage: {afterConnectionsMemory - initialMemory:N0} bytes for {connectionCount} connections"
        );
        Console.WriteLine($"Memory per connection: {memoryPerConnection:N0} bytes");

        // Each connection should use less than 100KB (reasonable for channels and metadata)
        Assert.IsTrue(
            memoryPerConnection < 100_000,
            $"Memory usage per connection too high: {memoryPerConnection:N0} bytes"
        );
    }

    [TestMethod]
    [TestCategory("Performance")]
    public async Task ConcurrentOperations_ShouldMaintainPerformance()
    {
        // Arrange
        var connectionCount = 200;
        var groupCount = 10;
        var messagesPerGroup = 5;

        var connections = new List<string>();
        for (int i = 0; i < connectionCount; i++)
        {
            connections.Add(await _notificationService.ConnectAsync($"concurrent-conn-{i}"));
        }

        // Distribute connections across groups
        var tasks = new List<Task>();
        for (int g = 0; g < groupCount; g++)
        {
            var groupName = $"concurrent-group-{g}";
            var groupConnections = connections
                .Skip(g * (connectionCount / groupCount))
                .Take(connectionCount / groupCount);

            foreach (var conn in groupConnections)
            {
                tasks.Add(_groupService.JoinGroupAsync(conn, groupName).AsTask());
            }
        }

        await Task.WhenAll(tasks);
        tasks.Clear();

        var stopwatch = Stopwatch.StartNew();

        // Act - Concurrent operations: group broadcasts, individual messages, stats queries
        for (int i = 0; i < messagesPerGroup; i++)
        {
            // Group broadcasts
            for (int g = 0; g < groupCount; g++)
            {
                tasks.Add(
                    _groupService
                        .PublishToGroupAsync($"concurrent-group-{g}", $"Group message {i}")
                        .AsTask()
                );
            }

            // Individual messages
            for (int c = 0; c < Math.Min(50, connectionCount); c++)
            {
                tasks.Add(
                    _notificationService
                        .PublishToConnectionAsync(connections[c], $"Individual message {i}")
                        .AsTask()
                );
            }

            // Stats queries
            tasks.Add(_notificationService.GetStatsAsync());
            tasks.Add(_groupService.GetGroupStatisticsAsync());
        }

        await Task.WhenAll(tasks);
        stopwatch.Stop();

        // Assert
        var operationsPerSecond = tasks.Count / stopwatch.Elapsed.TotalSeconds;
        Console.WriteLine(
            $"Completed {tasks.Count} concurrent operations in {stopwatch.ElapsedMilliseconds}ms"
        );
        Console.WriteLine($"Rate: {operationsPerSecond:F0} operations/second");

        // Should handle at least 100 concurrent operations per second
        Assert.IsTrue(
            operationsPerSecond > 100,
            $"Concurrent operation rate too slow: {operationsPerSecond:F0}/sec"
        );
    }

    [TestMethod]
    [TestCategory("Performance")]
    public async Task MessageLatency_ShouldBeLow()
    {
        // Arrange
        var connectionId = await _notificationService.ConnectAsync("latency-test-conn");
        var reader = _notificationService.Subscribe(connectionId);
        var messageCount = 100;
        var latencies = new List<TimeSpan>();

        // Act - Measure message delivery latency
        for (int i = 0; i < messageCount; i++)
        {
            var sendTime = DateTime.UtcNow;
            var message = $"Latency test message {i} - {sendTime:O}";

            await _notificationService.PublishToConnectionAsync(connectionId, message);

            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var receivedMessage = await reader.ReadAsync(cts.Token);
            var receiveTime = DateTime.UtcNow;

            var latency = receiveTime - sendTime;
            latencies.Add(latency);
        }

        // Assert
        var averageLatency = TimeSpan.FromTicks((long)latencies.Average(l => l.Ticks));
        var maxLatency = latencies.Max();
        var p95Latency = latencies.OrderBy(l => l).Skip((int)(messageCount * 0.95)).First();

        Console.WriteLine($"Average latency: {averageLatency.TotalMilliseconds:F2}ms");
        Console.WriteLine($"Max latency: {maxLatency.TotalMilliseconds:F2}ms");
        Console.WriteLine($"95th percentile latency: {p95Latency.TotalMilliseconds:F2}ms");

        // Average latency should be under 50ms, 95th percentile under 100ms
        Assert.IsTrue(
            averageLatency.TotalMilliseconds < 50,
            $"Average latency too high: {averageLatency.TotalMilliseconds:F2}ms"
        );
        Assert.IsTrue(
            p95Latency.TotalMilliseconds < 100,
            $"95th percentile latency too high: {p95Latency.TotalMilliseconds:F2}ms"
        );
    }
}
