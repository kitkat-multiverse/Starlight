using Starlight.Protobuf.Fixtures;
using Starlight.Rpc;
using Starlight.Rpc.Proto;
using Starlight.Rpc.Tunnel;
using Xunit;

namespace Starlight.Tests;

// ── Helpers ──────────────────────────────────────────────────────────────────

file static class Pair
{
    public static (RpcTunnel client, RpcTunnel server) Create() => DirectTunnel.CreatePair();
}

// ── Unit tests — pub/sub ──────────────────────────────────────────────────────

public sealed class TunnelPubSubTests
{
    [Fact]
    public async Task Publish_IntId_DeliversToPeer()
    {
        var (client, server) = Pair.Create();
        var frame = new Frame { SequenceId = 99 };
        Frame? received = null;

        server.Subscribe<Frame>(id: 5, (msg, _) => {
            received = msg;
            return Task.CompletedTask;
        });
        await client.Publish(id: 5, frame);

        Assert.Same(frame, received);
    }

    [Fact]
    public async Task Publish_StringId_DeliversToPeer()
    {
        var (client, server) = Pair.Create();
        var frame = new Frame { SentMs = 42 };
        Frame? received = null;

        server.Subscribe<Frame>("control.cmd", (msg, _) => {
            received = msg;
            return Task.CompletedTask;
        });
        await client.Publish("control.cmd", frame);

        Assert.Same(frame, received);
    }

    [Fact]
    public async Task Publish_BothDirections()
    {
        var (client, server) = Pair.Create();
        var outbound = new Frame { SequenceId = 1 };
        var inbound = new Frame { SequenceId = 2 };
        Frame? clientReceived = null, serverReceived = null;

        client.Subscribe<Frame>(id: 1, (msg, _) => {
            clientReceived = msg;
            return Task.CompletedTask;
        });

        server.Subscribe<Frame>(id: 1, (msg, _) => {
            serverReceived = msg;
            return Task.CompletedTask;
        });

        await client.Publish(id: 1, outbound);
        await server.Publish(id: 1, inbound);

        Assert.Same(outbound, serverReceived);
        Assert.Same(inbound, clientReceived);
    }

    [Fact]
    public async Task IntAndStringFrequencies_AreIsolated()
    {
        var (client, server) = Pair.Create();
        var intFired = false;
        var strFired = false;

        server.Subscribe(id: 5, _ => {
            intFired = true;
            return Task.CompletedTask;
        });

        server.Subscribe("5", _ => {
            strFired = true;
            return Task.CompletedTask;
        });

        await client.Publish(id: 5, new Frame());

        Assert.True(intFired);
        Assert.False(strFired);
    }

    [Fact]
    public async Task Subscribe_MultipleHandlersSameId_AllFire()
    {
        var (client, server) = Pair.Create();
        var count = 0;

        server.Subscribe<Frame>(id: 7, (_, _) => {
            Interlocked.Increment(ref count);
            return Task.CompletedTask;
        });

        server.Subscribe<Frame>(id: 7, (_, _) => {
            Interlocked.Increment(ref count);
            return Task.CompletedTask;
        });

        await client.Publish(id: 7, new Frame());

        Assert.Equal(expected: 2, count);
    }

    [Fact]
    public async Task DisposedSubscription_StopsReceiving()
    {
        var (client, server) = Pair.Create();
        var count = 0;

        var sub = server.Subscribe<Frame>(id: 3, (_, _) => {
            Interlocked.Increment(ref count);
            return Task.CompletedTask;
        });
        await client.Publish(id: 3, new Frame());
        sub.Dispose();
        await client.Publish(id: 3, new Frame());

        Assert.Equal(expected: 1, count);
    }

    [Fact]
    public async Task Publish_NoSubscribers_DoesNotThrow()
    {
        var (client, _) = Pair.Create();
        await client.Publish(id: 99, new Frame());
        await client.Publish("nobody", new Frame());
    }
}

// ── Unit tests — decode ───────────────────────────────────────────────────────

public sealed class TunnelDecodeTests
{
    [Fact]
    public async Task TryDecode_Generic_ReturnsSameInstance()
    {
        var (client, server) = Pair.Create();
        var frame = new Frame { Flags = 7 };
        TunnelMessage? raw = null;

        server.Subscribe(id: 1, msg => {
            raw = msg;
            return Task.CompletedTask;
        });
        await client.Publish(id: 1, frame);

        Assert.NotNull(raw);
        Assert.Same(frame, raw!.TryDecode<Frame>());
    }

    [Fact]
    public async Task TryDecode_ByType_ReturnsSameInstance()
    {
        var (client, server) = Pair.Create();
        var frame = new Frame { Length = 12 };
        TunnelMessage? raw = null;

        server.Subscribe(id: 1, msg => {
            raw = msg;
            return Task.CompletedTask;
        });
        await client.Publish(id: 1, frame);

        Assert.NotNull(raw);
        Assert.Same(frame, raw!.TryDecode(typeof(Frame)));
    }

    [Fact]
    public async Task TryDecode_WrongType_ReturnsNull()
    {
        var (client, server) = Pair.Create();
        TunnelMessage? raw = null;

        server.Subscribe(id: 1, msg => {
            raw = msg;
            return Task.CompletedTask;
        });
        await client.Publish(id: 1, new Frame());

        Assert.Null(raw!.TryDecode<Crate>());
        Assert.Null(raw!.TryDecode(typeof(Crate)));
    }
}

// ── Unit tests — request/reply ────────────────────────────────────────────────

public sealed class TunnelRequestReplyTests
{
    [Fact]
    public async Task Request_IntId_ReturnsReply()
    {
        var (client, server) = Pair.Create();

        server.Subscribe<Frame>(id: 10, async (req, raw) => { await raw.Reply(new Frame { SequenceId = req.SequenceId + 1 }); });

        var reply = await client.Request<Frame>(id: 10, new Frame { SequenceId = 5 });

        Assert.Equal(expected: 6u, reply.SequenceId);
    }

    [Fact]
    public async Task Request_StringId_ReturnsReply()
    {
        var (client, server) = Pair.Create();

        server.Subscribe<Frame>("ping", async (req, raw) => { await raw.Reply(new Frame { SentMs = req.SentMs * 2 }); });

        var reply = await client.Request<Frame>("ping", new Frame { SentMs = 21 });

        Assert.Equal(expected: 42UL, reply.SentMs);
    }

    [Fact]
    public async Task Request_ResponderNeverReplies_TimesOut()
    {
        var (client, server) = Pair.Create();
        server.Subscribe(id: 1, _ => Task.CompletedTask);

        await Assert.ThrowsAsync<TunnelRequestTimeoutException>(() =>
            client.Request<Frame>(id: 1, new Frame(), TimeSpan.FromMilliseconds(50)));
    }
}

// ── Unit tests — closure ──────────────────────────────────────────────────────

public sealed class TunnelClosureTests
{
    [Fact]
    public void Close_TripsBothClosedTokens()
    {
        var (client, server) = Pair.Create();
        Assert.False(client.IsClosed);
        Assert.False(server.IsClosed);

        client.Close();

        Assert.True(client.IsClosed);
        Assert.True(server.IsClosed);
    }

    [Fact]
    public void Close_FiresPeerOnClosedEvent()
    {
        var (client, server) = Pair.Create();
        var fired = false;
        server.OnClosed += () => fired = true;

        client.Close();

        Assert.True(fired);
    }

    [Fact]
    public void Close_IsIdempotent()
    {
        var (client, _) = Pair.Create();
        client.Close();
        client.Close(); // must not throw or double-fire
    }

    [Fact]
    public async Task Publish_AfterClose_ThrowsTunnelClosedException()
    {
        var (client, _) = Pair.Create();
        client.Close();

        await Assert.ThrowsAsync<TunnelClosedException>(() =>
            client.Publish(id: 1, new Frame()));

        await Assert.ThrowsAsync<TunnelClosedException>(() =>
            client.Publish("x", new Frame()));
    }
}

// ── Unit tests — broker ───────────────────────────────────────────────────────

public sealed class TunnelBrokerTests
{
    [Fact]
    public void Register_Claim_RoundTrips()
    {
        var broker = new DirectTunnelBroker();
        var (_, server) = Pair.Create();

        var handle = broker.Register(server);
        var claimed = broker.Claim(handle);

        Assert.Same(server, claimed);
    }

    [Fact]
    public void Claim_SecondCall_ReturnsNull()
    {
        var broker = new DirectTunnelBroker();
        var (_, server) = Pair.Create();
        var handle = broker.Register(server);

        broker.Claim(handle);
        var second = broker.Claim(handle);

        Assert.Null(second);
    }

    [Fact]
    public void Claim_UnknownHandle_ReturnsNull()
    {
        var broker = new DirectTunnelBroker();
        Assert.Null(broker.Claim(Guid.NewGuid()));
    }

    [Fact]
    public async Task Register_Unclaimed_ExpiresAndCloses()
    {
        var broker = new DirectTunnelBroker(TimeSpan.FromMilliseconds(50));
        var (client, server) = Pair.Create();

        var handle = broker.Register(client);
        await Task.Delay(200);

        Assert.Null(broker.Claim(handle));
        Assert.True(client.IsClosed);
        Assert.True(server.IsClosed);
    }

    [Fact]
    public async Task Claim_DisarmsTtl()
    {
        var broker = new DirectTunnelBroker(TimeSpan.FromMilliseconds(50));
        var (client, _) = Pair.Create();

        var handle = broker.Register(client);
        var claimed = broker.Claim(handle);
        await Task.Delay(200);

        Assert.Same(client, claimed);
        Assert.False(client.IsClosed);
    }
}

// ── Integration tests — full handshake ───────────────────────────────────────

public sealed class TunnelHandshakeTests
{
    private static (DirectRpcTransport rpc, ITunnelBroker broker, TunnelClient client, TunnelHost host) Build()
    {
        var rpc = new DirectRpcTransport();
        var broker = new DirectTunnelBroker();
        var tunnelClient = new TunnelClient(rpc, new DirectTunnelConnector(broker));
        var tunnelHost = new TunnelHost(rpc, new DirectTunnelAcceptor(broker));
        return (rpc, broker, tunnelClient, tunnelHost);
    }

    [Fact]
    public async Task OpenTunnel_MessagesFlowBothWays()
    {
        var (_, _, client, host) = Build();

        RpcTunnel? serverEnd = null;

        host.TunnelOpened += (end, _) => {
            serverEnd = end;
            return Task.CompletedTask;
        };
        await host.Listen("game");

        var clientEnd = await client.Open("game");
        Assert.NotNull(serverEnd);

        Frame? serverReceived = null, clientReceived = null;

        serverEnd!.Subscribe<Frame>(id: 1, (msg, _) => {
            serverReceived = msg;
            return Task.CompletedTask;
        });

        clientEnd.Subscribe<Frame>(id: 2, (msg, _) => {
            clientReceived = msg;
            return Task.CompletedTask;
        });

        var outbound = new Frame { SequenceId = 10 };
        var inbound = new Frame { SequenceId = 20 };
        await clientEnd.Publish(id: 1, outbound);
        await serverEnd.Publish(id: 2, inbound);

        Assert.Same(outbound, serverReceived);
        Assert.Same(inbound, clientReceived);
    }

    [Fact]
    public async Task OpenTunnel_NoHost_ThrowsNoRespondersException()
    {
        var (_, _, client, _) = Build();

        await Assert.ThrowsAsync<NoRespondersException>(() =>
            client.Open("game", reqTimeout: TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task OpenTunnel_SubjectFiltering_OtherSubjectTimesOut()
    {
        var (_, _, client, host) = Build();

        host.TunnelOpened += (_, _) => Task.CompletedTask;
        await host.Listen("game");

        // A host IS listening on rpc.tunnel, but it ignores "lobby" subjects.
        // HasResponders is true, so the request is sent but never answered → timeout.
        await Assert.ThrowsAsync<RequestTimeoutException>(() =>
            client.Open("lobby", reqTimeout: TimeSpan.FromMilliseconds(100)));
    }

    [Fact]
    public async Task OpenTunnel_WithSorter_CollectsAllReplies()
    {
        var (rpc, broker, client, _) = Build();

        // Two hosts listening on the same subject simulate two game servers.
        using var h1 = new TunnelHost(rpc, new DirectTunnelAcceptor(broker));
        using var h2 = new TunnelHost(rpc, new DirectTunnelAcceptor(broker));
        h1.TunnelOpened += (_, _) => Task.CompletedTask;
        h2.TunnelOpened += (_, _) => Task.CompletedTask;
        await h1.Listen("game");
        await h2.Listen("game");

        IReadOnlyList<NewTunnelRsp>? captured = null;

        var clientEnd = await client.Open(
            "game",
            collectWindow: TimeSpan.FromMilliseconds(300),
            sorter: list => {
                captured = list;
                return list[^1];
            });

        Assert.NotNull(captured);
        Assert.Equal(expected: 2, captured!.Count);
        Assert.NotNull(clientEnd);
    }
}
