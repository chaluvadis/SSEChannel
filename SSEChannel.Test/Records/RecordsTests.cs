using System.ComponentModel.DataAnnotations;
using SSEChannel.Core.Records;

namespace SSEChannel.Test.Records;

[TestClass]
public class RecordsTests
{
    [TestMethod]
    public void IncomingMessage_ShouldGenerateUniqueIds()
    {
        // Act
        var message1 = new IncomingMessage { Message = "Test message 1" };
        var message2 = new IncomingMessage { Message = "Test message 2" };

        // Assert
        Assert.AreNotEqual(message1.Id, message2.Id);
        Assert.AreNotEqual(Guid.Empty, message1.Id);
        Assert.AreNotEqual(Guid.Empty, message2.Id);
    }

    [TestMethod]
    public void IncomingMessage_WithValidMessage_ShouldPassValidation()
    {
        // Arrange
        var message = new IncomingMessage
        {
            Message = "This is a valid message with more than 10 characters",
        };
        var context = new ValidationContext(message);
        var results = new List<ValidationResult>();

        // Act
        var isValid = Validator.TryValidateObject(message, context, results, true);

        // Assert
        Assert.IsTrue(isValid);
        Assert.AreEqual(0, results.Count);
    }

    [TestMethod]
    public void IncomingMessage_WithEmptyMessage_ShouldFailValidation()
    {
        // Arrange
        var message = new IncomingMessage { Message = "" };
        var context = new ValidationContext(message);
        var results = new List<ValidationResult>();

        // Act
        var isValid = Validator.TryValidateObject(message, context, results, true);

        // Assert
        Assert.IsFalse(isValid);
        Assert.IsTrue(results.Any(r => r.ErrorMessage!.Contains("Empty messages are not allowed")));
    }

    [TestMethod]
    public void IncomingMessage_WithShortMessage_ShouldFailValidation()
    {
        // Arrange
        var message = new IncomingMessage { Message = "Short" };
        var context = new ValidationContext(message);
        var results = new List<ValidationResult>();

        // Act
        var isValid = Validator.TryValidateObject(message, context, results, true);

        // Assert
        Assert.IsFalse(isValid);
        Assert.IsTrue(results.Any(r => r.ErrorMessage!.Contains("at least 10 characters")));
    }

    [TestMethod]
    public void IncomingMessageResponse_ShouldInitializeCorrectly()
    {
        // Arrange
        var message = "Test message";
        var status = "Received";
        var receivedDate = DateTime.Now;

        // Act
        var response = new IncomingMessageResponse(message, status, receivedDate);

        // Assert
        Assert.AreEqual(message, response.Message);
        Assert.AreEqual(status, response.Status);
        Assert.AreEqual(receivedDate, response.ReceivedDate);
    }

    [TestMethod]
    public void ConnectRequest_WithConnectionId_ShouldInitializeCorrectly()
    {
        // Arrange
        var connectionId = "test-connection-123";

        // Act
        var request = new ConnectRequest(connectionId);

        // Assert
        Assert.AreEqual(connectionId, request.ConnectionId);
    }

    [TestMethod]
    public void ConnectRequest_WithoutConnectionId_ShouldBeNull()
    {
        // Act
        var request = new ConnectRequest();

        // Assert
        Assert.IsNull(request.ConnectionId);
    }

    [TestMethod]
    public void GroupOperationResponse_WithSuccess_ShouldInitializeCorrectly()
    {
        // Arrange
        var success = true;
        var message = "Operation successful";
        var groupName = "test-group";
        var connectionId = "conn-123";
        var timestamp = DateTime.UtcNow;

        // Act
        var response = new GroupOperationResponse(
            success,
            message,
            groupName,
            connectionId,
            timestamp
        );

        // Assert
        Assert.AreEqual(success, response.Success);
        Assert.AreEqual(message, response.Message);
        Assert.AreEqual(groupName, response.GroupName);
        Assert.AreEqual(connectionId, response.ConnectionId);
        Assert.AreEqual(timestamp, response.Timestamp);
        Assert.IsNull(response.Error);
    }

    [TestMethod]
    public void GroupOperationResponse_WithError_ShouldInitializeCorrectly()
    {
        // Arrange
        var success = false;
        var message = "Operation failed";
        var groupName = "test-group";
        var connectionId = "conn-123";
        var timestamp = DateTime.UtcNow;
        var error = "Connection not found";

        // Act
        var response = new GroupOperationResponse(
            success,
            message,
            groupName,
            connectionId,
            timestamp,
            error
        );

        // Assert
        Assert.AreEqual(success, response.Success);
        Assert.AreEqual(message, response.Message);
        Assert.AreEqual(groupName, response.GroupName);
        Assert.AreEqual(connectionId, response.ConnectionId);
        Assert.AreEqual(timestamp, response.Timestamp);
        Assert.AreEqual(error, response.Error);
    }

    [TestMethod]
    public void GroupBroadcastResponse_ShouldInitializeCorrectly()
    {
        // Arrange
        var success = true;
        var message = "Broadcast successful";
        var groupName = "broadcast-group";
        var memberCount = 5;
        var broadcastMessage = "Hello group!";
        var timestamp = DateTime.UtcNow;

        // Act
        var response = new GroupBroadcastResponse(
            success,
            message,
            groupName,
            memberCount,
            broadcastMessage,
            timestamp
        );

        // Assert
        Assert.AreEqual(success, response.Success);
        Assert.AreEqual(message, response.Message);
        Assert.AreEqual(groupName, response.GroupName);
        Assert.AreEqual(memberCount, response.MemberCount);
        Assert.AreEqual(broadcastMessage, response.BroadcastMessage);
        Assert.AreEqual(timestamp, response.Timestamp);
        Assert.IsNull(response.Error);
    }

    [TestMethod]
    public void GroupListResponse_ShouldInitializeCorrectly()
    {
        // Arrange
        var success = true;
        var groups = new[] { "group1", "group2", "group3" };
        var totalCount = groups.Length;
        var timestamp = DateTime.UtcNow;

        // Act
        var response = new GroupListResponse(success, groups, totalCount, timestamp);

        // Assert
        Assert.AreEqual(success, response.Success);
        CollectionAssert.AreEqual(groups, response.Groups);
        Assert.AreEqual(totalCount, response.TotalCount);
        Assert.AreEqual(timestamp, response.Timestamp);
        Assert.IsNull(response.Error);
    }

    [TestMethod]
    public void GroupInfoResponse_ShouldInitializeCorrectly()
    {
        // Arrange
        var success = true;
        var groupName = "info-group";
        var exists = true;
        var memberCount = 3;
        var members = new[] { "conn1", "conn2", "conn3" };
        var timestamp = DateTime.UtcNow;

        // Act
        var response = new GroupInfoResponse(
            success,
            groupName,
            exists,
            memberCount,
            members,
            timestamp
        );

        // Assert
        Assert.AreEqual(success, response.Success);
        Assert.AreEqual(groupName, response.GroupName);
        Assert.AreEqual(exists, response.Exists);
        Assert.AreEqual(memberCount, response.MemberCount);
        CollectionAssert.AreEqual(members, response.Members);
        Assert.AreEqual(timestamp, response.Timestamp);
        Assert.IsNull(response.Error);
    }

    [TestMethod]
    public void GroupStatisticsResponse_ShouldInitializeCorrectly()
    {
        // Arrange
        var success = true;
        var statistics = new Dictionary<string, int>
        {
            ["group1"] = 5,
            ["group2"] = 3,
            ["group3"] = 8,
        };
        var totalGroups = statistics.Count;
        var totalMembers = statistics.Values.Sum();
        var timestamp = DateTime.UtcNow;

        // Act
        var response = new GroupStatisticsResponse(
            success,
            statistics,
            totalGroups,
            totalMembers,
            timestamp
        );

        // Assert
        Assert.AreEqual(success, response.Success);
        CollectionAssert.AreEqual(statistics, response.Statistics);
        Assert.AreEqual(totalGroups, response.TotalGroups);
        Assert.AreEqual(totalMembers, response.TotalMembers);
        Assert.AreEqual(timestamp, response.Timestamp);
        Assert.IsNull(response.Error);
    }

    [TestMethod]
    public void MessageRequest_ShouldInitializeCorrectly()
    {
        // Arrange
        var message = "Test message content";

        // Act
        var request = new MessageRequest(message);

        // Assert
        Assert.AreEqual(message, request.Message);
    }

    [TestMethod]
    public void GroupJoinRequest_ShouldInitializeCorrectly()
    {
        // Arrange
        var connectionId = "conn-123";

        // Act
        var request = new GroupJoinRequest(connectionId);

        // Assert
        Assert.AreEqual(connectionId, request.ConnectionId);
    }

    [TestMethod]
    public void GroupLeaveRequest_ShouldInitializeCorrectly()
    {
        // Arrange
        var connectionId = "conn-456";

        // Act
        var request = new GroupLeaveRequest(connectionId);

        // Assert
        Assert.AreEqual(connectionId, request.ConnectionId);
    }

    [TestMethod]
    public void GroupBroadcastRequest_ShouldInitializeCorrectly()
    {
        // Arrange
        var message = "Broadcast message content";

        // Act
        var request = new GroupBroadcastRequest(message);

        // Assert
        Assert.AreEqual(message, request.Message);
    }
}
