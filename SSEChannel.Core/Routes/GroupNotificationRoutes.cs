namespace SSEChannel.Core.Routes;

/// <summary>
/// Dedicated routes for group notification management and operations
/// </summary>
public static class GroupNotificationRoutes
{
    public static void MapGroupNotificationRoutes(this WebApplication app)
    {
        var groupApi = app.MapGroup("/api/groups").WithTags("Group Management");

        // Group membership management
        groupApi
            .MapPost("/{groupName}/join", JoinGroup)
            .WithName("JoinGroup")
            .WithSummary("Join a notification group")
            .WithDescription("Add a connection to a named group for targeted messaging");

        groupApi
            .MapPost("/{groupName}/leave", LeaveGroup)
            .WithName("LeaveGroup")
            .WithSummary("Leave a notification group")
            .WithDescription("Remove a connection from a named group");

        groupApi
            .MapDelete("/connections/{connectionId}/all", RemoveFromAllGroups)
            .WithName("RemoveFromAllGroups")
            .WithSummary("Remove connection from all groups")
            .WithDescription("Remove a connection from all groups (used during disconnect)");

        // Group messaging
        groupApi
            .MapPost("/{groupName}/broadcast", BroadcastToGroup)
            .WithName("BroadcastToGroup")
            .WithSummary("Broadcast message to group")
            .WithDescription("Send a message to all members of a specific group");

        // Group information and queries
        groupApi
            .MapGet("/", GetAllGroups)
            .WithName("GetAllGroups")
            .WithSummary("Get all groups")
            .WithDescription("Retrieve a list of all existing groups");

        groupApi
            .MapGet("/{groupName}", GetGroupInfo)
            .WithName("GetGroupInfo")
            .WithSummary("Get group information")
            .WithDescription("Get detailed information about a specific group");

        groupApi
            .MapGet("/{groupName}/members", GetGroupMembers)
            .WithName("GetGroupMembers")
            .WithSummary("Get group members")
            .WithDescription("Retrieve all connections in a specific group");

        groupApi
            .MapGet("/connections/{connectionId}/groups", GetConnectionGroups)
            .WithName("GetConnectionGroups")
            .WithSummary("Get connection groups")
            .WithDescription("Get all groups that a connection belongs to");

        // Group statistics and monitoring
        groupApi
            .MapGet("/statistics", GetGroupStatistics)
            .WithName("GetGroupStatistics")
            .WithSummary("Get group statistics")
            .WithDescription("Retrieve statistics about all groups");

        groupApi
            .MapGet("/{groupName}/count", GetGroupMemberCount)
            .WithName("GetGroupMemberCount")
            .WithSummary("Get group member count")
            .WithDescription("Get the number of members in a specific group");

        // Group maintenance
        groupApi
            .MapPost("/maintenance/cleanup", CleanupEmptyGroups)
            .WithName("CleanupEmptyGroups")
            .WithSummary("Cleanup empty groups")
            .WithDescription("Remove all empty groups (maintenance operation)");

        groupApi
            .MapPost("/maintenance/comprehensive-cleanup", PerformComprehensiveCleanup)
            .WithName("PerformComprehensiveCleanup")
            .WithSummary("Perform comprehensive cleanup")
            .WithDescription("Clean up orphaned connections and empty groups");

        groupApi
            .MapGet("/maintenance/cleanup-stats", GetCleanupStats)
            .WithName("GetCleanupStats")
            .WithSummary("Get cleanup statistics")
            .WithDescription("Retrieve statistics about cleanup operations");

        groupApi
            .MapPost("/maintenance/reset-cleanup-stats", ResetCleanupStats)
            .WithName("ResetCleanupStats")
            .WithSummary("Reset cleanup statistics")
            .WithDescription("Reset cleanup operation counters");

        groupApi
            .MapGet("/{groupName}/exists", CheckGroupExists)
            .WithName("CheckGroupExists")
            .WithSummary("Check if group exists")
            .WithDescription("Check whether a specific group exists");
    }

    private static async Task<IResult> JoinGroup(
        IGroupNotificationService groupService,
        string groupName,
        GroupJoinRequest request
    )
    {
        try
        {
            await groupService.JoinGroupAsync(request.ConnectionId, groupName);
            return Results.Ok(
                new GroupOperationResponse(
                    Success: true,
                    Message: $"Successfully joined group '{groupName}'",
                    GroupName: groupName,
                    ConnectionId: request.ConnectionId,
                    Timestamp: DateTime.UtcNow
                )
            );
        }
        catch (Exception ex)
        {
            return Results.BadRequest(
                new GroupOperationResponse(
                    Success: false,
                    Message: $"Failed to join group '{groupName}': {ex.Message}",
                    GroupName: groupName,
                    ConnectionId: request.ConnectionId,
                    Timestamp: DateTime.UtcNow
                )
            );
        }
    }

    private static async Task<IResult> LeaveGroup(
        IGroupNotificationService groupService,
        string groupName,
        GroupLeaveRequest request
    )
    {
        try
        {
            await groupService.LeaveGroupAsync(request.ConnectionId, groupName);
            return Results.Ok(
                new GroupOperationResponse(
                    Success: true,
                    Message: $"Successfully left group '{groupName}'",
                    GroupName: groupName,
                    ConnectionId: request.ConnectionId,
                    Timestamp: DateTime.UtcNow
                )
            );
        }
        catch (Exception ex)
        {
            return Results.BadRequest(
                new GroupOperationResponse(
                    Success: false,
                    Message: $"Failed to leave group '{groupName}': {ex.Message}",
                    GroupName: groupName,
                    ConnectionId: request.ConnectionId,
                    Timestamp: DateTime.UtcNow
                )
            );
        }
    }

    private static async Task<IResult> RemoveFromAllGroups(
        IGroupNotificationService groupService,
        string connectionId
    )
    {
        try
        {
            await groupService.RemoveFromAllGroupsAsync(connectionId);
            return Results.Ok(
                new GroupOperationResponse(
                    Success: true,
                    Message: $"Successfully removed connection from all groups",
                    GroupName: null,
                    ConnectionId: connectionId,
                    Timestamp: DateTime.UtcNow
                )
            );
        }
        catch (Exception ex)
        {
            return Results.BadRequest(
                new GroupOperationResponse(
                    Success: false,
                    Message: $"Failed to remove connection from all groups: {ex.Message}",
                    GroupName: null,
                    ConnectionId: connectionId,
                    Timestamp: DateTime.UtcNow
                )
            );
        }
    }

    private static async Task<IResult> BroadcastToGroup(
        IGroupNotificationService groupService,
        string groupName,
        GroupBroadcastRequest request
    )
    {
        try
        {
            await groupService.PublishToGroupAsync(groupName, request.Message);
            var memberCount = await groupService.GetGroupMemberCountAsync(groupName);

            return Results.Ok(
                new GroupBroadcastResponse(
                    Success: true,
                    Message: $"Message broadcast to group '{groupName}' successfully",
                    GroupName: groupName,
                    MemberCount: memberCount,
                    BroadcastMessage: request.Message,
                    Timestamp: DateTime.UtcNow
                )
            );
        }
        catch (Exception ex)
        {
            return Results.BadRequest(
                new GroupBroadcastResponse(
                    Success: false,
                    Message: $"Failed to broadcast to group '{groupName}': {ex.Message}",
                    GroupName: groupName,
                    MemberCount: 0,
                    BroadcastMessage: request.Message,
                    Timestamp: DateTime.UtcNow
                )
            );
        }
    }

    private static async Task<IResult> GetAllGroups(IGroupNotificationService groupService)
    {
        try
        {
            var groups = await groupService.GetAllGroupsAsync();
            return Results.Ok(
                new GroupListResponse(
                    Success: true,
                    Groups: groups.ToArray(),
                    TotalCount: groups.Count,
                    Timestamp: DateTime.UtcNow
                )
            );
        }
        catch (Exception ex)
        {
            return Results.BadRequest(
                new GroupListResponse(
                    Success: false,
                    Groups: [],
                    TotalCount: 0,
                    Timestamp: DateTime.UtcNow,
                    Error: ex.Message
                )
            );
        }
    }

    private static async Task<IResult> GetGroupInfo(
        IGroupNotificationService groupService,
        string groupName
    )
    {
        try
        {
            var exists = await groupService.GroupExistsAsync(groupName);
            if (!exists)
            {
                return Results.NotFound(
                    new GroupInfoResponse(
                        Success: false,
                        GroupName: groupName,
                        Exists: false,
                        MemberCount: 0,
                        Members: Array.Empty<string>(),
                        Timestamp: DateTime.UtcNow,
                        Error: $"Group '{groupName}' does not exist"
                    )
                );
            }

            var memberCount = await groupService.GetGroupMemberCountAsync(groupName);
            var members = await groupService.GetConnectionsInGroupAsync(groupName);

            return Results.Ok(
                new GroupInfoResponse(
                    Success: true,
                    GroupName: groupName,
                    Exists: true,
                    MemberCount: memberCount,
                    Members: members.ToArray(),
                    Timestamp: DateTime.UtcNow
                )
            );
        }
        catch (Exception ex)
        {
            return Results.BadRequest(
                new GroupInfoResponse(
                    Success: false,
                    GroupName: groupName,
                    Exists: false,
                    MemberCount: 0,
                    Members: Array.Empty<string>(),
                    Timestamp: DateTime.UtcNow,
                    Error: ex.Message
                )
            );
        }
    }

    private static async Task<IResult> GetGroupMembers(
        IGroupNotificationService groupService,
        string groupName
    )
    {
        try
        {
            var members = await groupService.GetConnectionsInGroupAsync(groupName);
            return Results.Ok(
                new GroupMembersResponse(
                    Success: true,
                    GroupName: groupName,
                    Members: [.. members],
                    MemberCount: members.Count,
                    Timestamp: DateTime.UtcNow
                )
            );
        }
        catch (Exception ex)
        {
            return Results.BadRequest(
                new GroupMembersResponse(
                    Success: false,
                    GroupName: groupName,
                    Members: Array.Empty<string>(),
                    MemberCount: 0,
                    Timestamp: DateTime.UtcNow,
                    Error: ex.Message
                )
            );
        }
    }

    private static async Task<IResult> GetConnectionGroups(
        IGroupNotificationService groupService,
        string connectionId
    )
    {
        try
        {
            var groups = await groupService.GetGroupsForConnectionAsync(connectionId);
            return Results.Ok(
                new ConnectionGroupsResponse(
                    Success: true,
                    ConnectionId: connectionId,
                    Groups: groups.ToArray(),
                    GroupCount: groups.Count,
                    Timestamp: DateTime.UtcNow
                )
            );
        }
        catch (Exception ex)
        {
            return Results.BadRequest(
                new ConnectionGroupsResponse(
                    Success: false,
                    ConnectionId: connectionId,
                    Groups: Array.Empty<string>(),
                    GroupCount: 0,
                    Timestamp: DateTime.UtcNow,
                    Error: ex.Message
                )
            );
        }
    }

    private static async Task<IResult> GetGroupStatistics(IGroupNotificationService groupService)
    {
        try
        {
            var statistics = await groupService.GetGroupStatisticsAsync();
            return Results.Ok(
                new GroupStatisticsResponse(
                    Success: true,
                    Statistics: statistics.ToDictionary(kvp => kvp.Key, kvp => kvp.Value),
                    TotalGroups: statistics.Count,
                    TotalMembers: statistics.Values.Sum(),
                    Timestamp: DateTime.UtcNow
                )
            );
        }
        catch (Exception ex)
        {
            return Results.BadRequest(
                new GroupStatisticsResponse(
                    Success: false,
                    Statistics: new Dictionary<string, int>(),
                    TotalGroups: 0,
                    TotalMembers: 0,
                    Timestamp: DateTime.UtcNow,
                    Error: ex.Message
                )
            );
        }
    }

    private static async Task<IResult> GetGroupMemberCount(
        IGroupNotificationService groupService,
        string groupName
    )
    {
        try
        {
            var count = await groupService.GetGroupMemberCountAsync(groupName);
            return Results.Ok(
                new GroupCountResponse(
                    Success: true,
                    GroupName: groupName,
                    MemberCount: count,
                    Timestamp: DateTime.UtcNow
                )
            );
        }
        catch (Exception ex)
        {
            return Results.BadRequest(
                new GroupCountResponse(
                    Success: false,
                    GroupName: groupName,
                    MemberCount: 0,
                    Timestamp: DateTime.UtcNow,
                    Error: ex.Message
                )
            );
        }
    }

    private static async Task<IResult> CleanupEmptyGroups(IGroupNotificationService groupService)
    {
        try
        {
            await groupService.RemoveEmptyGroupsAsync();
            return Results.Ok(
                new GroupMaintenanceResponse(
                    Success: true,
                    Message: "Empty groups cleanup completed successfully",
                    Timestamp: DateTime.UtcNow
                )
            );
        }
        catch (Exception ex)
        {
            return Results.BadRequest(
                new GroupMaintenanceResponse(
                    Success: false,
                    Message: $"Failed to cleanup empty groups: {ex.Message}",
                    Timestamp: DateTime.UtcNow
                )
            );
        }
    }

    private static async Task<IResult> PerformComprehensiveCleanup(
        IGroupNotificationService groupService
    )
    {
        try
        {
            await groupService.PerformComprehensiveCleanupAsync();
            return Results.Ok(
                new GroupMaintenanceResponse(
                    Success: true,
                    Message: "Comprehensive cleanup completed successfully",
                    Timestamp: DateTime.UtcNow
                )
            );
        }
        catch (Exception ex)
        {
            return Results.BadRequest(
                new GroupMaintenanceResponse(
                    Success: false,
                    Message: $"Failed to perform comprehensive cleanup: {ex.Message}",
                    Timestamp: DateTime.UtcNow
                )
            );
        }
    }

    private static async Task<IResult> GetCleanupStats(IGroupNotificationService groupService)
    {
        try
        {
            var stats = await groupService.GetCleanupStatsAsync();
            return Results.Ok(
                new
                {
                    Success = true,
                    CleanupStats = stats,
                    Timestamp = DateTime.UtcNow,
                }
            );
        }
        catch (Exception ex)
        {
            return Results.BadRequest(
                new
                {
                    Success = false,
                    Message = $"Failed to get cleanup statistics: {ex.Message}",
                    Timestamp = DateTime.UtcNow,
                }
            );
        }
    }

    private static Task<IResult> ResetCleanupStats(IGroupNotificationService groupService)
    {
        try
        {
            groupService.ResetCleanupStats();
            return Task.FromResult(
                Results.Ok(
                    new GroupMaintenanceResponse(
                        Success: true,
                        Message: "Cleanup statistics reset successfully",
                        Timestamp: DateTime.UtcNow
                    )
                )
            );
        }
        catch (Exception ex)
        {
            return Task.FromResult(
                Results.BadRequest(
                    new GroupMaintenanceResponse(
                        Success: false,
                        Message: $"Failed to reset cleanup statistics: {ex.Message}",
                        Timestamp: DateTime.UtcNow
                    )
                )
            );
        }
    }

    private static async Task<IResult> CheckGroupExists(
        IGroupNotificationService groupService,
        string groupName
    )
    {
        try
        {
            var exists = await groupService.GroupExistsAsync(groupName);
            return Results.Ok(
                new GroupExistsResponse(
                    Success: true,
                    GroupName: groupName,
                    Exists: exists,
                    Timestamp: DateTime.UtcNow
                )
            );
        }
        catch (Exception ex)
        {
            return Results.BadRequest(
                new GroupExistsResponse(
                    Success: false,
                    GroupName: groupName,
                    Exists: false,
                    Timestamp: DateTime.UtcNow,
                    Error: ex.Message
                )
            );
        }
    }
}
