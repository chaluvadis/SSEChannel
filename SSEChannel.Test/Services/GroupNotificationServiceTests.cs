using Microsoft.Extensions.Logging;
using SSEChannel.Core.Services;
using SSEChannel.Core.Services.Interfaces;

namespace SSEChannel.Test.Services;

[TestClass]
public class GroupNotificationServiceTests
{
    private IGroupNotificationService _groupService = null!;
    private ILogger<GroupNotificationService> _logger = null!;
    private readonly List<string> _receivedMessages = [];
    private readonly Dictionary<string, List<string>> _connectionMessages = [];

    [TestInitialize]
    public void Setup()
    {
        _logger = new LoggerFactory().CreateLogger<GroupNotificationService>();

        // Mock the publish function to capture messages
        _groupService = new GroupNotificationService(
            (connectionId, message) =>
            {
                _receivedMessages.Add(message);
                if (!_connectionMessages.ContainsKey(connectionId))
                    _connectionMessages[connectionId] = [];
                _connectionMessages[connectionId].Add(message);
                return ValueTask.CompletedTask;
            },
            _logger
        );
    }

    [TestCleanup]
    public void Cleanup()
    {
        _receivedMessages.Clear();
        _connectionMessages.Clear();
    }

    [TestMethod]
    public async Task JoinGroupAsync_ShouldAddConnectionToGroup()
    {
        // Arrange
        var connectionId = "conn-1";
        var groupName = "test-group";

        // Act
        await _groupService.JoinGroupAsync(connectionId, groupName);

        // Assert
        var groups = await _groupService.GetGroupsForConnectionAsync(connectionId);
        CollectionAssert.Contains(groups.ToList(), groupName);

        var connections = await _groupService.GetConnectionsInGroupAsync(groupName);
        CollectionAssert.Contains(connections.ToList(), connectionId);
    }

    [TestMethod]
    public async Task LeaveGroupAsync_ShouldRemoveConnectionFromGroup()
    {
        // Arrange
        var connectionId = "conn-1";
        var groupName = "test-group";
        await _groupService.JoinGroupAsync(connectionId, groupName);

        // Act
        await _groupService.LeaveGroupAsync(connectionId, groupName);

        // Assert
        var groups = await _groupService.GetGroupsForConnectionAsync(connectionId);
        CollectionAssert.DoesNotContain(groups.ToList(), groupName);

        var connections = await _groupService.GetConnectionsInGroupAsync(groupName);
        CollectionAssert.DoesNotContain(connections.ToList(), connectionId);
    }

    [TestMethod]
    public async Task PublishToGroupAsync_ShouldDeliverToAllGroupMembers()
    {
        // Arrange
        var groupName = "broadcast-group";
        var connections = new[] { "conn-1", "conn-2", "conn-3" };
        var testMessage = "Group broadcast message";

        foreach (var conn in connections)
        {
            await _groupService.JoinGroupAsync(conn, groupName);
        }

        // Act
        await _groupService.PublishToGroupAsync(groupName, testMessage);

        // Assert
        Assert.AreEqual(connections.Length, _receivedMessages.Count);
        foreach (var conn in connections)
        {
            Assert.IsTrue(_connectionMessages.ContainsKey(conn));
            CollectionAssert.Contains(_connectionMessages[conn], testMessage);
        }
    }

    [TestMethod]
    public async Task GetGroupMemberCountAsync_ShouldReturnCorrectCount()
    {
        // Arrange
        var groupName = "count-test-group";
        var connections = new[] { "conn-1", "conn-2", "conn-3", "conn-4" };

        foreach (var conn in connections)
        {
            await _groupService.JoinGroupAsync(conn, groupName);
        }

        // Act
        var count = await _groupService.GetGroupMemberCountAsync(groupName);

        // Assert
        Assert.AreEqual(connections.Length, count);
    }

    [TestMethod]
    public async Task GroupExistsAsync_ShouldReturnCorrectStatus()
    {
        // Arrange
        var existingGroup = "existing-group";
        var nonExistentGroup = "non-existent-group";

        await _groupService.JoinGroupAsync("conn-1", existingGroup);

        // Act & Assert
        var exists = await _groupService.GroupExistsAsync(existingGroup);
        Assert.IsTrue(exists);

        var notExists = await _groupService.GroupExistsAsync(nonExistentGroup);
        Assert.IsFalse(notExists);
    }

    [TestMethod]
    public async Task GetAllGroupsAsync_ShouldReturnAllGroups()
    {
        // Arrange
        var groups = new[] { "group-1", "group-2", "group-3" };

        foreach (var group in groups)
        {
            await _groupService.JoinGroupAsync("conn-1", group);
        }

        // Act
        var allGroups = await _groupService.GetAllGroupsAsync();

        // Assert
        Assert.AreEqual(groups.Length, allGroups.Count);
        foreach (var group in groups)
        {
            CollectionAssert.Contains(allGroups.ToList(), group);
        }
    }

    [TestMethod]
    public async Task RemoveFromAllGroupsAsync_ShouldRemoveConnectionFromAllGroups()
    {
        // Arrange
        var connectionId = "conn-1";
        var groups = new[] { "group-1", "group-2", "group-3" };

        foreach (var group in groups)
        {
            await _groupService.JoinGroupAsync(connectionId, group);
        }

        // Act
        await _groupService.RemoveFromAllGroupsAsync(connectionId);

        // Assert
        var remainingGroups = await _groupService.GetGroupsForConnectionAsync(connectionId);
        Assert.AreEqual(0, remainingGroups.Count);

        foreach (var group in groups)
        {
            var connections = await _groupService.GetConnectionsInGroupAsync(group);
            CollectionAssert.DoesNotContain(connections.ToList(), connectionId);
        }
    }

    [TestMethod]
    public async Task GetGroupStatisticsAsync_ShouldReturnCorrectStatistics()
    {
        // Arrange
        var groupData = new Dictionary<string, string[]>
        {
            ["group-1"] = ["conn-1", "conn-2"],
            ["group-2"] = ["conn-1", "conn-3", "conn-4"],
            ["group-3"] = ["conn-5"],
        };

        foreach (var (group, connections) in groupData)
        {
            foreach (var conn in connections)
            {
                await _groupService.JoinGroupAsync(conn, group);
            }
        }

        // Act
        var stats = await _groupService.GetGroupStatisticsAsync();

        // Assert
        Assert.AreEqual(groupData.Count, stats.Count);
        foreach (var (group, expectedConnections) in groupData)
        {
            Assert.IsTrue(stats.ContainsKey(group));
            Assert.AreEqual(expectedConnections.Length, stats[group]);
        }
    }

    [TestMethod]
    public async Task RemoveEmptyGroupsAsync_ShouldRemoveGroupsWithNoMembers()
    {
        // Arrange
        var connectionId = "conn-1";
        var groupName = "temp-group";

        await _groupService.JoinGroupAsync(connectionId, groupName);
        await _groupService.LeaveGroupAsync(connectionId, groupName);

        // Act
        await _groupService.RemoveEmptyGroupsAsync();

        // Assert
        var allGroups = await _groupService.GetAllGroupsAsync();
        CollectionAssert.DoesNotContain(allGroups.ToList(), groupName);
    }

    [TestMethod]
    public async Task ConcurrentGroupOperations_ShouldHandleMultipleOperations()
    {
        // Arrange
        var groupName = "concurrent-group";
        var connections = Enumerable.Range(1, 50).Select(i => $"conn-{i}").ToArray();

        // Act
        var joinTasks = connections.Select(conn =>
            _groupService.JoinGroupAsync(conn, groupName).AsTask()
        );
        await Task.WhenAll(joinTasks);

        // Assert
        var memberCount = await _groupService.GetGroupMemberCountAsync(groupName);
        Assert.AreEqual(connections.Length, memberCount);

        var groupConnections = await _groupService.GetConnectionsInGroupAsync(groupName);
        Assert.AreEqual(connections.Length, groupConnections.Count);
    }

    [TestMethod]
    public async Task MultipleGroupMembership_ShouldAllowConnectionInMultipleGroups()
    {
        // Arrange
        var connectionId = "multi-conn";
        var groups = new[] { "group-a", "group-b", "group-c" };

        // Act
        foreach (var group in groups)
        {
            await _groupService.JoinGroupAsync(connectionId, group);
        }

        // Assert
        var connectionGroups = await _groupService.GetGroupsForConnectionAsync(connectionId);
        Assert.AreEqual(groups.Length, connectionGroups.Count);

        foreach (var group in groups)
        {
            CollectionAssert.Contains(connectionGroups.ToList(), group);
        }
    }

    [TestMethod]
    public async Task PublishToEmptyGroup_ShouldNotThrow()
    {
        // Arrange
        var emptyGroup = "empty-group";
        var message = "Message to empty group";

        // Act & Assert
        await _groupService.PublishToGroupAsync(emptyGroup, message);
        Assert.AreEqual(0, _receivedMessages.Count);
    }

    [TestMethod]
    public async Task JoinSameGroupTwice_ShouldNotDuplicate()
    {
        // Arrange
        var connectionId = "conn-1";
        var groupName = "duplicate-test";

        // Act
        await _groupService.JoinGroupAsync(connectionId, groupName);
        await _groupService.JoinGroupAsync(connectionId, groupName);

        // Assert
        var memberCount = await _groupService.GetGroupMemberCountAsync(groupName);
        Assert.AreEqual(1, memberCount);

        var connections = await _groupService.GetConnectionsInGroupAsync(groupName);
        Assert.AreEqual(1, connections.Count);
    }
}
