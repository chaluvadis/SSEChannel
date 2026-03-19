using SSEChannel.Test.TestHelpers;

namespace SSEChannel.Test.Services;

[TestClass]
public class NotificationServiceComprehensiveTests
{
    [TestMethod]
    public async Task CompleteWorkflow_ShouldHandleAllOperations()
    {
        // Arrange
        var (notificationService, groupService) = TestUtilities.CreateIntegratedServices();
        var groupName = "workflow-test-group";
        var connectionCount = 10;

        try
        {
            // Act 1: Create connections
            var (connectionIds, readers) = await TestUtilities.CreateMultipleConnectionsAsync(
                notificationService,
                connectionCount,
                "workflow-conn"
            );

            // Act 2: Create group with members
            await TestUtilities.CreateGroupWithMembersAsync(groupService, groupName, connectionIds);

            // Act 3: Send group message
            var groupMessage = "Workflow group message";
            await groupService.PublishToGroupAsync(groupName, groupMessage);

            // Assert 1: All connections receive group message
            await TestUtilities.VerifyAllReceiveMessageAsync(readers, groupMessage);

            // Act 4: Send individual messages
            var individualTasks = connectionIds.Select(
                async (conn, index) =>
                {
                    var message = $"Individual message {index}";
                    await notificationService.PublishToConnectionAsync(conn, message);
                    return (conn, message);
                }
            );

            var individualMessages = await Task.WhenAll(individualTasks);

            // Assert 2: Each connection receives its individual message
            for (int i = 0; i < connectionIds.Count; i++)
            {
                var expectedMessage = $"Individual message {i}";
                var actualMessage = await TestUtilities.WaitForMessageAsync(readers[i]);
                Assert.AreEqual(expectedMessage, actualMessage);
            }

            // Act 5: Broadcast to all
            var broadcastMessage = "Broadcast to all";
            await notificationService.PublishToAllAsync(broadcastMessage);

            // Assert 3: All connections receive broadcast
            await TestUtilities.VerifyAllReceiveMessageAsync(readers, broadcastMessage);

            // Act 6: Remove some connections from group
            var halfCount = connectionCount / 2;
            var removeFromGroupTasks = connectionIds
                .Take(halfCount)
                .Select(conn => groupService.LeaveGroupAsync(conn, groupName).AsTask());
            await Task.WhenAll(removeFromGroupTasks);

            // Act 7: Send another group message
            var secondGroupMessage = "Second group message";
            await groupService.PublishToGroupAsync(groupName, secondGroupMessage);

            // Assert 4: Only remaining group members receive the message
            var remainingReaders = readers.Skip(halfCount).ToList();
            await TestUtilities.VerifyAllReceiveMessageAsync(remainingReaders, secondGroupMessage);

            // Assert 5: Removed members don't receive the message (timeout expected)
            var removedReaders = readers.Take(halfCount).ToList();
            foreach (var reader in removedReaders)
            {
                var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
                var hasMessage = reader.TryRead(out _);
                Assert.IsFalse(
                    hasMessage,
                    "Removed group member should not receive group messages"
                );
            }

            // Act 8: Verify statistics
            var stats = await notificationService.GetStatsAsync();
            var groupStats = await groupService.GetGroupStatisticsAsync();

            // Assert 6: Statistics are correct
            Assert.IsTrue(stats.ActiveConnections >= connectionCount);
            Assert.IsTrue(groupStats.ContainsKey(groupName));
            Assert.AreEqual(connectionCount - halfCount, groupStats[groupName]);
        }
        finally
        {
            // Cleanup
            (notificationService as IDisposable)?.Dispose();
        }
    }

    [TestMethod]
    public async Task ErrorHandling_ShouldHandleInvalidOperations()
    {
        // Arrange
        var (notificationService, groupService) = TestUtilities.CreateIntegratedServices();

        try
        {
            // Act & Assert: Invalid connection operations should not throw
            await notificationService.PublishToConnectionAsync("non-existent-conn", "test message");
            await notificationService.DisconnectAsync("non-existent-conn");

            // Act & Assert: Invalid group operations should not throw
            await groupService.JoinGroupAsync("non-existent-conn", "test-group");
            await groupService.LeaveGroupAsync("non-existent-conn", "test-group");
            await groupService.PublishToGroupAsync("non-existent-group", "test message");

            // Act & Assert: Query operations on non-existent entities should return empty/default
            var groups = await groupService.GetGroupsForConnectionAsync("non-existent-conn");
            Assert.AreEqual(0, groups.Count);

            var connections = await groupService.GetConnectionsInGroupAsync("non-existent-group");
            Assert.AreEqual(0, connections.Count);

            var memberCount = await groupService.GetGroupMemberCountAsync("non-existent-group");
            Assert.AreEqual(0, memberCount);

            var exists = await groupService.GroupExistsAsync("non-existent-group");
            Assert.IsFalse(exists);
        }
        finally
        {
            (notificationService as IDisposable)?.Dispose();
        }
    }

    [TestMethod]
    public async Task StressTest_ShouldHandleHighLoad()
    {
        // Arrange
        var (notificationService, groupService) = TestUtilities.CreateIntegratedServices();
        var connectionCount = 100;
        var groupCount = 10;
        var messagesPerGroup = 5;

        try
        {
            // Act 1: Create many connections
            var elapsed = await TestUtilities.Performance.MeasureAsync(async () =>
            {
                var (connectionIds, readers) = await TestUtilities.CreateMultipleConnectionsAsync(
                    notificationService,
                    connectionCount,
                    "stress-conn"
                );

                // Distribute connections across groups
                var connectionsPerGroup = connectionCount / groupCount;
                var groupTasks = new List<Task>();

                for (int g = 0; g < groupCount; g++)
                {
                    var groupName = $"stress-group-{g}";
                    var groupConnections = connectionIds
                        .Skip(g * connectionsPerGroup)
                        .Take(connectionsPerGroup);

                    groupTasks.Add(
                        TestUtilities.CreateGroupWithMembersAsync(
                            groupService,
                            groupName,
                            groupConnections
                        )
                    );
                }

                await Task.WhenAll(groupTasks);

                // Send messages to all groups concurrently
                var messageTasks = new List<Task>();
                for (int g = 0; g < groupCount; g++)
                {
                    var groupName = $"stress-group-{g}";
                    for (int m = 0; m < messagesPerGroup; m++)
                    {
                        messageTasks.Add(
                            groupService.PublishToGroupAsync(groupName, $"Stress message {m}").AsTask()
                        );
                    }
                }

                await Task.WhenAll(messageTasks);
            });

            // Assert: Performance should be reasonable
            TestUtilities.Performance.AssertPerformance(
                elapsed,
                TimeSpan.FromSeconds(30),
                "Stress test"
            );

            // Verify final state
            var stats = await notificationService.GetStatsAsync();
            var groupStats = await groupService.GetGroupStatisticsAsync();

            Assert.IsTrue(stats.ActiveConnections >= connectionCount);
            Assert.AreEqual(groupCount, groupStats.Count);
        }
        finally
        {
            (notificationService as IDisposable)?.Dispose();
        }
    }

    [TestMethod]
    public async Task ConcurrencyTest_ShouldHandleRaceConditions()
    {
        // Arrange
        var (notificationService, groupService) = TestUtilities.CreateIntegratedServices();
        var connectionCount = 50;
        var groupName = "concurrency-test-group";

        try
        {
            // Create connections
            var (connectionIds, readers) = await TestUtilities.CreateMultipleConnectionsAsync(
                notificationService,
                connectionCount,
                "concurrent-conn"
            );

            // Act: Perform many concurrent operations
            var tasks = new List<Task>();

            // Concurrent joins and leaves
            foreach (var conn in connectionIds)
            {
                tasks.Add(
                    Task.Run(async () =>
                    {
                        await groupService.JoinGroupAsync(conn, groupName);
                        await Task.Delay(10); // Small delay to create race conditions
                        await groupService.LeaveGroupAsync(conn, groupName);
                        await groupService.JoinGroupAsync(conn, groupName); // Rejoin
                    })
                );
            }

            // Concurrent message sending
            for (int i = 0; i < 20; i++)
            {
                var messageIndex = i;
                tasks.Add(
                    Task.Run(async () =>
                    {
                        await Task.Delay(5); // Stagger the messages
                        await groupService.PublishToGroupAsync(
                            groupName,
                            $"Concurrent message {messageIndex}"
                        );
                    })
                );
            }

            // Concurrent stats queries
            for (int i = 0; i < 10; i++)
            {
                tasks.Add(
                    Task.Run(async () =>
                    {
                        await groupService.GetGroupStatisticsAsync();
                        await notificationService.GetStatsAsync();
                    })
                );
            }

            await Task.WhenAll(tasks);

            // Assert: System should be in consistent state
            var finalMemberCount = await groupService.GetGroupMemberCountAsync(groupName);
            var groupConnections = await groupService.GetConnectionsInGroupAsync(groupName);

            Assert.AreEqual(finalMemberCount, groupConnections.Count);
            Assert.IsTrue(finalMemberCount <= connectionCount);
        }
        finally
        {
            (notificationService as IDisposable)?.Dispose();
        }
    }

    [TestMethod]
    public async Task ResourceCleanup_ShouldReleaseResources()
    {
        // Arrange
        var (notificationService, groupService) = TestUtilities.CreateIntegratedServices();
        var connectionCount = 20;
        var groupName = "cleanup-test-group";

        try
        {
            // Create connections and add to group
            var (connectionIds, readers) = await TestUtilities.CreateMultipleConnectionsAsync(
                notificationService,
                connectionCount,
                "cleanup-conn"
            );

            await TestUtilities.CreateGroupWithMembersAsync(groupService, groupName, connectionIds);

            var initialStats = await notificationService.GetStatsAsync();
            var initialGroupStats = await groupService.GetGroupStatisticsAsync();

            // Act: Disconnect all connections
            var disconnectTasks = connectionIds.Select(conn =>
                notificationService.DisconnectAsync(conn).AsTask()
            );
            await Task.WhenAll(disconnectTasks);

            // Remove from all groups
            var removeFromGroupTasks = connectionIds.Select(conn =>
                groupService.RemoveFromAllGroupsAsync(conn).AsTask()
            );
            await Task.WhenAll(removeFromGroupTasks);

            // Clean up empty groups
            await groupService.RemoveEmptyGroupsAsync();

            // Assert: Resources should be cleaned up
            var finalStats = await notificationService.GetStatsAsync();
            var finalGroupStats = await groupService.GetGroupStatisticsAsync();

            Assert.IsTrue(finalStats.ActiveConnections < initialStats.ActiveConnections);
            Assert.IsFalse(
                finalGroupStats.ContainsKey(groupName) && finalGroupStats[groupName] > 0
            );
        }
        finally
        {
            (notificationService as IDisposable)?.Dispose();
        }
    }
}
