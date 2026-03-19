namespace SSEChannel.Core.Records;

public record IncomingMessage
{
    public Guid Id { get; init; } = Guid.NewGuid();

    [Required(AllowEmptyStrings = false, ErrorMessage = "Empty messages are not allowed")]
    [MinLength(10, ErrorMessage = "Please send a valid message with at least 10 characters")]
    public string Message { get; set; } = string.Empty;
}

public record IncomingMessageResponse(string Message, string Status, DateTime ReceivedDate);

public record ConnectRequest(string? ConnectionId = null);

public record MessageRequest(string Message);

public record GroupJoinRequest(string ConnectionId);

public record GroupLeaveRequest(string ConnectionId);

public record GroupBroadcastRequest(string Message);
public record GroupOperationResponse(
    bool Success,
    string Message,
    string? GroupName,
    string ConnectionId,
    DateTime Timestamp,
    string? Error = null
);

public record GroupBroadcastResponse(
    bool Success,
    string Message,
    string GroupName,
    int MemberCount,
    string BroadcastMessage,
    DateTime Timestamp,
    string? Error = null
);

public record GroupListResponse(
    bool Success,
    string[] Groups,
    int TotalCount,
    DateTime Timestamp,
    string? Error = null
);

public record GroupInfoResponse(
    bool Success,
    string GroupName,
    bool Exists,
    int MemberCount,
    string[] Members,
    DateTime Timestamp,
    string? Error = null
);

public record GroupMembersResponse(
    bool Success,
    string GroupName,
    string[] Members,
    int MemberCount,
    DateTime Timestamp,
    string? Error = null
);

public record ConnectionGroupsResponse(
    bool Success,
    string ConnectionId,
    string[] Groups,
    int GroupCount,
    DateTime Timestamp,
    string? Error = null
);

public record GroupStatisticsResponse(
    bool Success,
    Dictionary<string, int> Statistics,
    int TotalGroups,
    int TotalMembers,
    DateTime Timestamp,
    string? Error = null
);

public record GroupCountResponse(
    bool Success,
    string GroupName,
    int MemberCount,
    DateTime Timestamp,
    string? Error = null
);

public record GroupMaintenanceResponse(
    bool Success,
    string Message,
    DateTime Timestamp,
    string? Error = null
);

public record GroupExistsResponse(
    bool Success,
    string GroupName,
    bool Exists,
    DateTime Timestamp,
    string? Error = null
);