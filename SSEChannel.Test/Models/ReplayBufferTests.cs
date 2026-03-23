using SSEChannel.Core.Models;

namespace SSEChannel.Test.Models;

[TestClass]
public class ReplayBufferTests
{
    [TestMethod]
    public void Add_SingleMessage_CountIsOne()
    {
        var buffer = new ReplayBuffer(5);
        buffer.Add(MakeMessage("a"));
        Assert.AreEqual(1, buffer.Count);
    }

    [TestMethod]
    public void Add_OverCapacity_CountStaysAtCapacity()
    {
        var buffer = new ReplayBuffer(3);
        for (int i = 0; i < 10; i++)
            buffer.Add(MakeMessage(i.ToString()));
        Assert.AreEqual(3, buffer.Count);
    }

    [TestMethod]
    public void GetSince_NullLastId_ReturnsAll()
    {
        var buffer = new ReplayBuffer(5);
        buffer.Add(MakeMessage("a"));
        buffer.Add(MakeMessage("b"));
        buffer.Add(MakeMessage("c"));

        var result = buffer.GetSince(null).ToList();
        Assert.AreEqual(3, result.Count);
    }

    [TestMethod]
    public void GetSince_EmptyBuffer_ReturnsEmpty()
    {
        var buffer = new ReplayBuffer(5);
        var result = buffer.GetSince(null).ToList();
        Assert.AreEqual(0, result.Count);
    }

    [TestMethod]
    public void GetSince_KnownId_ReturnsMessagesAfterIt()
    {
        var buffer = new ReplayBuffer(10);
        var msgs = new[] { MakeMessage("a"), MakeMessage("b"), MakeMessage("c") };
        foreach (var m in msgs) buffer.Add(m);

        var result = buffer.GetSince(msgs[0].Id).ToList();
        Assert.AreEqual(2, result.Count);
        Assert.AreEqual(msgs[1].Id, result[0].Id);
        Assert.AreEqual(msgs[2].Id, result[1].Id);
    }

    [TestMethod]
    public void GetSince_LastIdIsLastMessage_ReturnsEmpty()
    {
        var buffer = new ReplayBuffer(5);
        var msg = MakeMessage("x");
        buffer.Add(msg);

        var result = buffer.GetSince(msg.Id).ToList();
        Assert.AreEqual(0, result.Count);
    }

    [TestMethod]
    public void GetSince_UnknownId_ReturnsAll()
    {
        var buffer = new ReplayBuffer(5);
        buffer.Add(MakeMessage("a"));
        buffer.Add(MakeMessage("b"));

        var result = buffer.GetSince("unknown-id").ToList();
        Assert.AreEqual(2, result.Count);
    }

    [TestMethod]
    public void Add_WrapsAround_ReturnsCorrectOrder()
    {
        // Capacity 3; add 5 messages → oldest 2 are evicted
        var buffer = new ReplayBuffer(3);
        var msgs = Enumerable.Range(0, 5)
            .Select(i => MakeMessage(i.ToString()))
            .ToArray();
        foreach (var m in msgs) buffer.Add(m);

        var result = buffer.GetSince(null).ToList();
        Assert.AreEqual(3, result.Count);
        // Should contain the 3 most recent: msgs[2], msgs[3], msgs[4]
        Assert.AreEqual(msgs[2].Id, result[0].Id);
        Assert.AreEqual(msgs[3].Id, result[1].Id);
        Assert.AreEqual(msgs[4].Id, result[2].Id);
    }

    [TestMethod]
    public void ThrowsOnZeroCapacity()
    {
        Assert.ThrowsException<ArgumentOutOfRangeException>(() => new ReplayBuffer(0));
    }

    [TestMethod]
    public void ThrowsOnNegativeCapacity()
    {
        Assert.ThrowsException<ArgumentOutOfRangeException>(() => new ReplayBuffer(-1));
    }

    private static SseMessage MakeMessage(string id) => new()
    {
        Id = id,
        Channel = "test",
        EventName = "message",
        SerializedPayload = "{}",
        Timestamp = DateTimeOffset.UtcNow,
    };
}
