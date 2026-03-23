using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using SSEChannel.Core.Backplane;
using SSEChannel.Core.Channels;
using SSEChannel.Core.Connections;
using SSEChannel.Core.Models;

namespace SSEChannel.Test.Channels;

[TestClass]
public class SseChannelServiceTests
{
    private static SseChannelService BuildService(
        SseOptions? options = null,
        IChannelBackplane? backplane = null)
    {
        options ??= new SseOptions();
        backplane ??= new InMemoryChannelBackplane();
        var store = new SseClientStore(options.ReplayBufferSize);
        return new SseChannelService(store, backplane, options, NullLogger<SseChannelService>.Instance);
    }

    // ── ValidateChannelName ───────────────────────────────────────────────────

    [TestMethod]
    public async Task PublishAsync_EmptyChannel_ThrowsArgumentException()
    {
        var svc = BuildService();
        await svc.StartAsync(CancellationToken.None);
        try
        {
            await Assert.ThrowsExceptionAsync<ArgumentException>(() =>
                svc.PublishAsync("", "hello"));
        }
        finally
        {
            await svc.StopAsync(CancellationToken.None);
            svc.Dispose();
        }
    }

    [TestMethod]
    public async Task PublishAsync_ChannelNameTooLong_ThrowsArgumentException()
    {
        var options = new SseOptions { MaxChannelNameLength = 5 };
        var svc = BuildService(options);
        await svc.StartAsync(CancellationToken.None);
        try
        {
            await Assert.ThrowsExceptionAsync<ArgumentException>(() =>
                svc.PublishAsync("toolongname", "hello"));
        }
        finally
        {
            await svc.StopAsync(CancellationToken.None);
            svc.Dispose();
        }
    }

    // ── GetClientCount / GetChannels ──────────────────────────────────────────

    [TestMethod]
    public async Task GetClientCount_NoClients_ReturnsZero()
    {
        var svc = BuildService();
        await svc.StartAsync(CancellationToken.None);
        try
        {
            Assert.AreEqual(0, svc.GetClientCount("chan"));
        }
        finally
        {
            await svc.StopAsync(CancellationToken.None);
            svc.Dispose();
        }
    }

    [TestMethod]
    public async Task GetChannels_NoActivity_ReturnsEmpty()
    {
        var svc = BuildService();
        await svc.StartAsync(CancellationToken.None);
        try
        {
            Assert.AreEqual(0, svc.GetChannels().Count);
        }
        finally
        {
            await svc.StopAsync(CancellationToken.None);
            svc.Dispose();
        }
    }

    // ── PublishAsync + backplane delivery ─────────────────────────────────────

    [TestMethod]
    public async Task PublishAsync_MessageDeliveredToSubscriber()
    {
        var backplane = new InMemoryChannelBackplane();
        var options = new SseOptions();
        var store = new SseClientStore(options.ReplayBufferSize);
        var svc = new SseChannelService(store, backplane, options, NullLogger<SseChannelService>.Instance);

        await svc.StartAsync(CancellationToken.None);

        // Add a client directly via the store (bypasses the HTTP plumbing)
        var client = store.AddClient("events");

        try
        {
            await svc.PublishAsync("events", "world", "greet");

            // Give the in-process backplane a moment to deliver
            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            string? received = null;
            await foreach (var msg in client.Reader.ReadAllAsync(cts.Token))
            {
                received = msg;
                break;
            }

            Assert.IsNotNull(received);
            StringAssert.Contains(received, "event: greet");
            StringAssert.Contains(received, "\"world\"");
        }
        finally
        {
            store.RemoveClient("events", client.ClientId);
            await svc.StopAsync(CancellationToken.None);
            svc.Dispose();
        }
    }

    [TestMethod]
    public async Task PublishAsync_AddsMessageToReplayBuffer()
    {
        var options = new SseOptions { ReplayBufferSize = 10 };
        var store = new SseClientStore(options.ReplayBufferSize);
        var svc = new SseChannelService(
            store, new InMemoryChannelBackplane(), options, NullLogger<SseChannelService>.Instance);

        await svc.StartAsync(CancellationToken.None);
        try
        {
            await svc.PublishAsync("replay-test", "payload");

            var buffer = store.GetOrCreateReplayBuffer("replay-test");
            Assert.AreEqual(1, buffer.Count);
        }
        finally
        {
            await svc.StopAsync(CancellationToken.None);
            svc.Dispose();
        }
    }

    // ── SendFromClientAsync ───────────────────────────────────────────────────

    [TestMethod]
    public async Task SendFromClientAsync_DelegatesToPublish()
    {
        var options = new SseOptions { ReplayBufferSize = 10 };
        var store = new SseClientStore(options.ReplayBufferSize);
        var svc = new SseChannelService(
            store, new InMemoryChannelBackplane(), options, NullLogger<SseChannelService>.Instance);

        await svc.StartAsync(CancellationToken.None);
        try
        {
            await svc.SendFromClientAsync("ch", "client-payload", "client-event");

            var buffer = store.GetOrCreateReplayBuffer("ch");
            Assert.AreEqual(1, buffer.Count);
        }
        finally
        {
            await svc.StopAsync(CancellationToken.None);
            svc.Dispose();
        }
    }
}
