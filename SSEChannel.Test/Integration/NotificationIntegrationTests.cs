using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using SSEChannel.Core.Services;
using SSEChannel.Core.Services.Interfaces;
using SSEChannel.Test.TestHelpers;

namespace SSEChannel.Test.Integration;

[TestClass]
public class NotificationIntegrationTests
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
    public async Task EndToEndGroupMessaging_ShouldWorkCorrectly()
    {
        // Arrange
        var groupName = "integration-test-group";
        var connections = new[]
        {
            await _notificationService.ConnectAsync("conn-1"),
            await _notificationService.ConnectAsync("conn-2"),
            await _notificationService.ConnectAsync("conn-3"),
        };

        var readers = connections.Select(conn => _notificationService.Subscribe(conn)).ToArray();
        var testMessage = "Integration test message";

        // Act - Join group
        foreach (var conn in connections)
        {
            await _groupService.JoinGroupAsync(conn, groupName);
        }

        // Act - Send group message
        await _groupService.PublishToGroupAsync(groupName, testMessage);

        // Assert - All connections should receive the message
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var receivedMessages = new List<string>();

        foreach (var reader in readers)
        {
            var message = await reader.ReadAsync(cts.Token);
            receivedMessages.Add(message);
        }

        Assert.AreEqual(connections.Length, receivedMessages.Count);
        Assert.IsTrue(receivedMessages.All(msg => msg == testMessage));
    }

    [TestMethod]
    public async Task MixedMessaging_IndividualAndGroup_ShouldWorkCorrectly()
    {
        // Arrange
        var groupName = "mixed-test-group";
        var conn1 = await _notificationService.ConnectAsync("mixed-conn-1");
        var conn2 = await _notificationService.ConnectAsync("mixed-conn-2");
        var conn3 = await _notificationService.ConnectAsync("mixed-conn-3");

        var reader1 = _notificationService.Subscribe(conn1);
        var reader2 = _notificationService.Subscribe(conn2);
        var reader3 = _notificationService.Subscribe(conn3);

        // Join conn1 and conn2 to group, leave conn3 out
        await _groupService.JoinGroupAsync(conn1, groupName);
        await _groupService.JoinGroupAsync(conn2, groupName);

        var groupMessage = "Group message";
        var individualMessage = "Individual message";

        // Act
        await _groupService.PublishToGroupAsync(groupName, groupMessage);
        await _notificationService.PublishToConnectionAsync(conn3, individualMessage);

        // Assert
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var msg1 = await reader1.ReadAsync(cts.Token);
        var msg2 = await reader2.ReadAsync(cts.Token);
        var msg3 = await reader3.ReadAsync(cts.Token);

        Assert.AreEqual(groupMessage, msg1);
        Assert.AreEqual(groupMessage, msg2);
        Assert.AreEqual(individualMessage, msg3);
    }

    [TestMethod]
    public async Task ConnectionCleanup_ShouldRemoveFromGroups()
    {
        // Arrange
        var groupName = "cleanup-test-group";
        var connectionId = await _notificationService.ConnectAsync("cleanup-conn");

        await _groupService.JoinGroupAsync(connectionId, groupName);
        var initialMemberCount = await _groupService.GetGroupMemberCountAsync(groupName);

        // Act
        await _notificationService.DisconnectAsync(connectionId);
        await _groupService.RemoveFromAllGroupsAsync(connectionId);

        // Assert
        var finalMemberCount = await _groupService.GetGroupMemberCountAsync(groupName);
        Assert.AreEqual(1, initialMemberCount);
        Assert.AreEqual(0, finalMemberCount);
    }

    [TestMethod]
    public async Task HighVolumeGroupMessaging_ShouldHandleLoad()
    {
        // Arrange
        var groupName = "load-test-group";
        var connectionCount = 20;
        var messageCount = 10;

        var connections = new List<string>();
        var readers = new List<ChannelReader<string>>();

        for (int i = 0; i < connectionCount; i++)
        {
            var conn = await _notificationService.ConnectAsync($"load-conn-{i}");
            connections.Add(conn);
            readers.Add(_notificationService.Subscribe(conn));
            await _groupService.JoinGroupAsync(conn, groupName);
        }

        // Act - Send multiple messages to the group
        var messages = Enumerable
            .Range(1, messageCount)
            .Select(i => $"Load test message {i}")
            .ToList();

        var publishTasks = messages.Select(msg =>
            _groupService.PublishToGroupAsync(groupName, msg).AsTask()
        );
        await Task.WhenAll(publishTasks);

        // Assert - Each connection should receive all messages
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var allReceivedMessages = new List<List<string>>();

        foreach (var reader in readers)
        {
            var connectionMessages = new List<string>();
            for (int i = 0; i < messageCount; i++)
            {
                var message = await reader.ReadAsync(cts.Token);
                connectionMessages.Add(message);
            }
            allReceivedMessages.Add(connectionMessages);
        }

        // Verify each connection received all messages
        Assert.AreEqual(connectionCount, allReceivedMessages.Count);
        foreach (var connectionMessages in allReceivedMessages)
        {
            Assert.AreEqual(messageCount, connectionMessages.Count);
            CollectionAssert.AreEquivalent(messages, connectionMessages);
        }
    }

    [TestMethod]
    public async Task MultipleGroupMembership_ShouldReceiveFromAllGroups()
    {
        // Arrange
        var connectionId = await _notificationService.ConnectAsync("multi-group-conn");
        var reader = _notificationService.Subscribe(connectionId);

        var groups = new[] { "group-a", "group-b", "group-c" };
        var messages = new[] { "Message A", "Message B", "Message C" };

        // Join all groups
        foreach (var group in groups)
        {
            await _groupService.JoinGroupAsync(connectionId, group);
        }

        // Act - Send message to each group
        for (int i = 0; i < groups.Length; i++)
        {
            await _groupService.PublishToGroupAsync(groups[i], messages[i]);
        }

        // Assert - Should receive all messages
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var receivedMessages = new List<string>();

        for (int i = 0; i < messages.Length; i++)
        {
            var message = await reader.ReadAsync(cts.Token);
            receivedMessages.Add(message);
        }

        Assert.AreEqual(messages.Length, receivedMessages.Count);
        CollectionAssert.AreEquivalent(messages, receivedMessages);
    }

    [TestMethod]
    public async Task ConcurrentGroupOperations_ShouldMaintainConsistency()
    {
        // Arrange
        var groupName = "concurrent-ops-group";
        var connectionCount = 30;
        var connections = new List<string>();

        for (int i = 0; i < connectionCount; i++)
        {
            connections.Add(await _notificationService.ConnectAsync($"concurrent-conn-{i}"));
        }

        // Act - Concurrent join operations
        var joinTasks = connections.Select(conn =>
            _groupService.JoinGroupAsync(conn, groupName).AsTask()
        );
        await Task.WhenAll(joinTasks);

        // Assert - All connections should be in the group
        var memberCount = await _groupService.GetGroupMemberCountAsync(groupName);
        var groupConnections = await _groupService.GetConnectionsInGroupAsync(groupName);

        Assert.AreEqual(connectionCount, memberCount);
        Assert.AreEqual(connectionCount, groupConnections.Count);

        // Act - Concurrent leave operations for half the connections
        var halfCount = connectionCount / 2;
        var leaveTasks = connections
            .Take(halfCount)
            .Select(conn => _groupService.LeaveGroupAsync(conn, groupName).AsTask());
        await Task.WhenAll(leaveTasks);

        // Assert - Only remaining connections should be in the group
        var finalMemberCount = await _groupService.GetGroupMemberCountAsync(groupName);
        Assert.AreEqual(connectionCount - halfCount, finalMemberCount);
    }

    [TestMethod]
    public async Task BroadcastToAll_WithGroupMembers_ShouldReachEveryone()
    {
        // Arrange
        var groupName = "broadcast-all-group";
        var groupConnections = new List<string>();
        var nonGroupConnections = new List<string>();
        var allReaders = new List<ChannelReader<string>>();

        // Create group members
        for (int i = 0; i < 3; i++)
        {
            var conn = await _notificationService.ConnectAsync($"group-member-{i}");
            groupConnections.Add(conn);
            allReaders.Add(_notificationService.Subscribe(conn));
            await _groupService.JoinGroupAsync(conn, groupName);
        }

        // Create non-group members
        for (int i = 0; i < 2; i++)
        {
            var conn = await _notificationService.ConnectAsync($"non-group-member-{i}");
            nonGroupConnections.Add(conn);
            allReaders.Add(_notificationService.Subscribe(conn));
        }

        var broadcastMessage = "Broadcast to all message";

        // Act
        await _notificationService.PublishToAllAsync(broadcastMessage);

        // Assert - All connections should receive the broadcast
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var receivedMessages = new List<string>();

        foreach (var reader in allReaders)
        {
            var message = await reader.ReadAsync(cts.Token);
            receivedMessages.Add(message);
        }

        var totalConnections = groupConnections.Count + nonGroupConnections.Count;
        Assert.AreEqual(totalConnections, receivedMessages.Count);
        Assert.IsTrue(receivedMessages.All(msg => msg == broadcastMessage));
    }
}
