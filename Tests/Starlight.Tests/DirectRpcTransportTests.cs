using Google.Protobuf.WellKnownTypes;
using Starlight.Rpc;
using Xunit;

namespace Starlight.Tests;

public sealed class DirectRpcTransportTests
{
    private const string Subject = "test.subject";

    [Fact]
    public async Task Publish_DeliversMessageToSubscriber()
    {
        var transport = new DirectRpcTransport();
        var payload = new byte[] { 1, 2, 3 };
        RpcMessage? received = null;

        await transport.Subscribe(Subject, msg => {
            received = msg;
            return Task.CompletedTask;
        });

        await transport.Publish(Subject, new RpcMessage(payload));

        Assert.NotNull(received);
        Assert.Equal(payload, received!.Payload);
    }

    [Fact]
    public async Task Publish_DeliversToAllSubscribers()
    {
        var transport = new DirectRpcTransport();
        var count = 0;

        await transport.Subscribe(Subject, _ => {
            Interlocked.Increment(ref count);
            return Task.CompletedTask;
        });

        await transport.Subscribe(Subject, _ => {
            Interlocked.Increment(ref count);
            return Task.CompletedTask;
        });

        await transport.Publish(Subject, new RpcMessage([]));

        Assert.Equal(expected: 2, count);
    }

    [Fact]
    public async Task Publish_NoSubscribers_DoesNotThrow()
    {
        var transport = new DirectRpcTransport();

        await transport.Publish("nobody.listening", new RpcMessage([]));
    }

    [Fact]
    public async Task Publish_OnlyDeliversToMatchingSubject()
    {
        var transport = new DirectRpcTransport();
        var received = false;

        await transport.Subscribe(Subject, _ => {
            received = true;
            return Task.CompletedTask;
        });

        await transport.Publish("other.subject", new RpcMessage([]));

        Assert.False(received);
    }

    [Fact]
    public async Task DisposedSubscription_StopsReceiving()
    {
        var transport = new DirectRpcTransport();
        var count = 0;

        var subscription = await transport.Subscribe(Subject, _ => {
            Interlocked.Increment(ref count);
            return Task.CompletedTask;
        });

        await transport.Publish(Subject, new RpcMessage([]));
        subscription.Dispose();
        await transport.Publish(Subject, new RpcMessage([]));

        Assert.Equal(expected: 1, count);
    }

    [Fact]
    public async Task Publish_Message_SerializesToDirectRpcMessageSharingMetadata()
    {
        var transport = new DirectRpcTransport();
        var sent = new StringValue { Value = "hello" };
        StringValue? received = null;

        await transport.Subscribe<StringValue>(Subject, msg => {
            received = msg;
            return Task.CompletedTask;
        });

        await transport.Publish(Subject, sent);

        // DirectRpcMessage stashes the protobuf object in metadata, so the
        // exact same instance should come back out without real deserialization.
        Assert.Same(sent, received);
    }

    [Fact]
    public async Task Request_ReturnsReplyFromHandler()
    {
        var transport = new DirectRpcTransport();

        await transport.Subscribe(Subject, async msg => {
            var request = msg.Deserialize<StringValue>();
            await msg.Reply(new StringValue { Value = $"echo:{request.Value}" });
        });

        var response = await transport.Request<StringValue, StringValue>(
            Subject, new StringValue { Value = "ping" });

        Assert.Equal("echo:ping", response.Value);
    }

    [Fact]
    public async Task Request_NoResponder_ThrowsImmediately()
    {
        var transport = new DirectRpcTransport();

        // No subscribers on the subject, so this must fail fast rather than
        // burning the full timeout window.
        await Assert.ThrowsAsync<NoRespondersException>(() =>
            transport.Request<StringValue, StringValue>(
                Subject, new StringValue { Value = "ping" },
                TimeSpan.FromSeconds(30)));
    }

    [Fact]
    public async Task Request_ResponderNeverReplies_TimesOut()
    {
        var transport = new DirectRpcTransport();

        // A responder exists but never replies, so the timeout path still applies.
        await transport.Subscribe(Subject, _ => Task.CompletedTask);

        await Assert.ThrowsAsync<RequestTimeoutException>(() =>
            transport.Request<StringValue, StringValue>(
                Subject, new StringValue { Value = "ping" },
                TimeSpan.FromMilliseconds(100)));
    }
}
