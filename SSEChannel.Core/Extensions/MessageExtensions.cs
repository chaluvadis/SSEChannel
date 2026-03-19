using SSEChannel.Core.Records;

namespace SSEChannel.Core.Extensions;

// Extension methods for IncomingMessage
public static class MessageExtensions
{
    extension(IncomingMessage source)
    {
        public IncomingMessageResponse ToResponse() => new(Message: source.Message, Status: "Received", ReceivedDate: DateTime.Now);
    }
    //public static IncomingMessageResponse ToResponse(this IncomingMessage source) =>
    //    new(Message: source.Message, Status: "Received", ReceivedDate: DateTime.Now);
}
