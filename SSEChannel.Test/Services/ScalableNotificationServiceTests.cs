using System.Threading.Channels;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SSEChannel.Core.Services;
using SSEChannel.Core.Services.Interfaces;
using SSEChannel.Test.TestHelpers;

namespace SSEChannel.Test.Services;

[TestClass]
public class ScalableNotificationServiceTests
{
    private IScalableNotificationService _service = null!;

    [TestInitialize]
    public void Setup()
    {
        _service = TestUtilities.CreateNotificationService();
    }

    [TestCleanup]
    public void Cleanup()
    {
        (_service as IDisposable)?.Dispose();
    }

    [TestMethod]
    public async Task ConnectAsync_ShouldReturnConnectionId()
    {
        // Act
        var connectionId = await _service.ConnectAsync();

        // Assert
        Assert.IsNotNull(connectionId);
        Assert.IsFalse(string.IsNullOrEmpty(connectionId));
    }

    [TestMethod]
    public async Task ConnectAsync_WithProvidedId_ShouldReturnSameId()
    {
        // Arrange
        var providedId = "test-connection-123";

        // Act
        var connectionId = await _service.ConnectAsync(providedId);

        // Assert
        Assert.AreEqual(providedId, connectionId);
    }

    [TestMethod]
    public async Task Subscribe_ShouldReturnChannelReader()
    {
        // Arrange
        var connectionId = await _service.ConnectAsync();

        // Act
        var reader = _service.Subscribe(connectionId);

        // Assert
        Assert.IsNotNull(reader);
        Assert.IsInstanceOfType<ChannelReader<string>>(reader);
    }

    [TestMethod]
    public async Task PublishToConnectionAsync_ShouldDeliverMessage()
    {
        // Arrange
        var connectionId = await _service.ConnectAsync();
        var reader = _service.Subscribe(connectionId);
        var testMessage = "Test message";

        // Act
        await _service.PublishToConnectionAsync(connectionId, testMessage);

        // Assert
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var receivedMessage = await reader.ReadAsync(cts.Token);
        Assert.AreEqual(testMessage, receivedMessage);
    }

    [TestMethod]
    public async Task PublishToAllAsync_ShouldDeliverToAllConnections()
    {
        // Arrange
        var connection1 = await _service.ConnectAsync();
        var connection2 = await _service.ConnectAsync();
        var reader1 = _service.Subscribe(connection1);
        var reader2 = _service.Subscribe(connection2);
        var testMessage = "Broadcast message";

        // Act
        await _service.PublishToAllAsync(testMessage);

        // Assert
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var message1 = await reader1.ReadAsync(cts.Token);
        var message2 = await reader2.ReadAsync(cts.Token);

        Assert.AreEqual(testMessage, message1);
        Assert.AreEqual(testMessage, message2);
    }

    [TestMethod]
    public async Task DisconnectAsync_ShouldRemoveConnection()
    {
        // Arrange
        var connectionId = await _service.ConnectAsync();
        var initialStats = await _service.GetStatsAsync();

        // Act
        await _service.DisconnectAsync(connectionId);
        var finalStats = await _service.GetStatsAsync();

        // Assert
        Assert.IsTrue(finalStats.ActiveConnections < initialStats.ActiveConnections);
    }

    [TestMethod]
    public async Task GetStatsAsync_ShouldReturnValidStats()
    {
        // Arrange
        await _service.ConnectAsync();
        await _service.ConnectAsync();

        // Act
        var stats = await _service.GetStatsAsync();

        // Assert
        Assert.IsNotNull(stats);
        Assert.IsTrue(stats.ActiveConnections >= 2);
        Assert.IsTrue(stats.TotalConnections >= 2);
        Assert.IsTrue(stats.LastUpdated <= DateTime.UtcNow);
    }

    [TestMethod]
    public async Task PublishToConnectionAsync_InvalidConnection_ShouldNotThrow()
    {
        // Arrange
        var invalidConnectionId = "non-existent-connection";
        var testMessage = "Test message";

        // Act & Assert
        await _service.PublishToConnectionAsync(invalidConnectionId, testMessage);
        // Should not throw exception
    }

    [TestMethod]
    public void Subscribe_InvalidConnection_ShouldReturnEmptyReader()
    {
        // Arrange
        var invalidConnectionId = "non-existent-connection";

        // Act
        var reader = _service.Subscribe(invalidConnectionId);

        // Assert
        Assert.IsNotNull(reader);
        Assert.IsFalse(reader.TryRead(out _));
    }

    [TestMethod]
    public async Task ConcurrentConnections_ShouldHandleMultipleConnections()
    {
        // Arrange
        var connectionTasks = new List<Task<string>>();
        var connectionCount = 100;

        // Act
        for (int i = 0; i < connectionCount; i++)
        {
            connectionTasks.Add(_service.ConnectAsync().AsTask());
        }

        var connectionIds = await Task.WhenAll(connectionTasks);

        // Assert
        Assert.AreEqual(connectionCount, connectionIds.Length);
        Assert.AreEqual(connectionCount, connectionIds.Distinct().Count());

        var stats = await _service.GetStatsAsync();
        Assert.IsTrue(stats.ActiveConnections >= connectionCount);
    }

    [TestMethod]
    public async Task ConcurrentPublishing_ShouldDeliverAllMessages()
    {
        // Arrange
        var connectionId = await _service.ConnectAsync();
        var reader = _service.Subscribe(connectionId);
        var messageCount = 50;
        var messages = Enumerable.Range(1, messageCount).Select(i => $"Message {i}").ToList();

        // Act
        var publishTasks = messages.Select(msg =>
            _service.PublishToConnectionAsync(connectionId, msg).AsTask()
        );
        await Task.WhenAll(publishTasks);

        // Assert
        var receivedMessages = new List<string>();
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        for (int i = 0; i < messageCount; i++)
        {
            var message = await reader.ReadAsync(cts.Token);
            receivedMessages.Add(message);
        }

        Assert.AreEqual(messageCount, receivedMessages.Count);
        CollectionAssert.AreEquivalent(messages, receivedMessages);
    }
}
