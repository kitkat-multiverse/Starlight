using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Starlight.Game;
using Starlight.Game.Ability;
using Starlight.Game.Modules;
using Starlight.Game.Player;
using Starlight.Game.Resources;
using Starlight.Game.World;
using Starlight.Protocol;
using Starlight.Rpc;
using Starlight.Rpc.Proto;
using Starlight.Rpc.Tunnel;
using Xunit;

namespace Starlight.Tests;

// ── Helpers ──────────────────────────────────────────────────────────────────

file static class Session
{
    /// <summary>Mirrors what <c>Program.cs</c> registers, so a broken component wiring fails here too.</summary>
    public static IServiceProvider Services()
    {
        var data = new GameData(new ConfigurationBuilder().Build());

        return new ServiceCollection()
            .AddLogging()
            .AddSingleton(data)
            .AddSingleton<AbilityInitializer>()
            .AddSingleton<RpcTransport, DirectRpcTransport>()
            .AddSingleton<PlayerManager>()
            .AddSingleton<WorldManager>()
            .BuildServiceProvider();
    }

    public static ModuleRegistry Registry() => new ModuleRegistry()
        .AddGameComponent()
        .AddWorldComponent()
        .Build();

    public static StarlightPlayer Player(IServiceProvider services, ModuleRegistry registry, uint uid)
    {
        var (_, server) = DirectTunnel.CreatePair();
        return new StarlightPlayer(services, registry, server) { Uid = uid };
    }
}

// ── Unit tests — peer ids ─────────────────────────────────────────────────────

public sealed class WorldPeerTests
{
    [Fact]
    public void PendingPlayer_WaitsForBornSelectionBeforeEnteringWorld()
    {
        var services = Session.Services();
        var registry = Session.Registry();
        var player = Session.Player(services, registry, uid: 1001);
        player.State.BornState = NetPlayerState.Types.PlayerBornState.Pending;

        player.Module<WorldModule>().OnLogin();
        var loginScene = player.Module<SceneModule>().OnLogin();

        Assert.Equal(expected: 0u, player.Module<WorldModule>().PeerId);
        Assert.Null(loginScene);

        var bornScene = player.Module<SceneModule>().OnBorn();

        Assert.Equal(expected: 1u, player.Module<WorldModule>().PeerId);
        Assert.Equal(expected: 3u, bornScene.SceneId);
    }

    [Fact]
    public void EnterOwnWorld_Owner_TakesHostPeerId()
    {
        var services = Session.Services();
        var registry = Session.Registry();

        var world = Session.Player(services, registry, uid: 1001).Module<WorldModule>();
        world.EnterOwnWorld();

        Assert.Equal(expected: 1u, world.PeerId);
        Assert.Equal(world.PeerId, world.World.HostPeerId);
    }

    [Fact]
    public void Enter_Guest_TakesNextPeerIdAndKeepsHostAuthority()
    {
        var services = Session.Services();
        var registry = Session.Registry();

        var host = Session.Player(services, registry, uid: 1001).Module<WorldModule>();
        var guest = Session.Player(services, registry, uid: 1002).Module<WorldModule>();

        host.EnterOwnWorld();
        guest.EnterOwnWorld();
        Assert.NotSame(host.World, guest.World);

        Assert.True(services.GetRequiredService<WorldManager>().TryGet(hostUid: 1001, out var target));
        guest.Enter(target);

        Assert.Same(host.World, guest.World);
        Assert.Equal(expected: 2u, guest.PeerId);
        Assert.Equal(expected: 1u, guest.World.HostPeerId);
        Assert.Equal(expected: 2, host.World.Peers.Count);
    }

    [Fact]
    public void Enter_LastPeerLeaving_ClosesTheEmptiedWorld()
    {
        var services = Session.Services();
        var registry = Session.Registry();
        var worlds = services.GetRequiredService<WorldManager>();

        var host = Session.Player(services, registry, uid: 1001).Module<WorldModule>();
        var guest = Session.Player(services, registry, uid: 1002).Module<WorldModule>();

        host.EnterOwnWorld();
        guest.EnterOwnWorld();

        Assert.True(worlds.TryGet(hostUid: 1001, out var target));
        guest.Enter(target);

        Assert.False(worlds.TryGet(hostUid: 1002, out _));
    }

    [Fact]
    public void Join_OwnerArrivingLast_DisplacesTheGuestHoldingTheirSlot()
    {
        var services = Session.Services();
        var registry = Session.Registry();

        var owner = Session.Player(services, registry, uid: 1001);
        var world = new World(owner);
        var guest = Session.Player(services, registry, uid: 1002);

        // A visitor reaching the world before its owner takes peer 1.
        Assert.Equal(expected: 1u, world.Join(guest));
        Assert.Equal(expected: 0u, world.HostPeerId);

        Assert.Equal(expected: 1u, world.Join(owner));
        Assert.Equal(expected: 1u, world.HostPeerId);
        Assert.Equal(expected: 2u, world.PeerIdOf(guest));
        Assert.Equal(expected: 2, world.Peers.Count);
    }

    [Fact]
    public void Join_AfterChurn_ReusesTheFreedSlotInsteadOfClimbing()
    {
        var services = Session.Services();
        var registry = Session.Registry();

        var owner = Session.Player(services, registry, uid: 1001);
        var world = new World(owner);
        world.Join(owner);

        var first = Session.Player(services, registry, uid: 1002);
        var second = Session.Player(services, registry, uid: 1003);
        world.Join(first);
        Assert.Equal(expected: 3u, world.Join(second));

        world.Leave(first);

        // The gap left at 2 gets filled before anything climbs past the seats in use.
        Assert.Equal(expected: 2u, world.Join(Session.Player(services, registry, uid: 1004)));
        Assert.Equal(expected: 3, world.Peers.Count);
    }

    [Fact]
    public void Join_SamePlayerTwice_KeepsOneSeat()
    {
        var services = Session.Services();
        var registry = Session.Registry();

        var owner = Session.Player(services, registry, uid: 1001);
        var world = new World(owner);
        var guest = Session.Player(services, registry, uid: 1002);

        world.Join(owner);
        world.Join(guest);
        world.Join(guest);

        Assert.Equal(expected: 2, world.Peers.Count);
        Assert.Equal(expected: 2u, world.PeerIdOf(guest));
    }

    [Fact]
    public void PeerId_GuestDisplacedByOwner_ReportsTheNewSlot()
    {
        var services = Session.Services();
        var registry = Session.Registry();
        var worlds = services.GetRequiredService<WorldManager>();

        var owner = Session.Player(services, registry, uid: 1001);
        var guest = Session.Player(services, registry, uid: 1002).Module<WorldModule>();

        // The guest arrives first, so the module hands out peer 1 until the owner shows up.
        guest.Enter(worlds.Open(owner));
        Assert.Equal(expected: 1u, guest.PeerId);

        owner.Module<WorldModule>().EnterOwnWorld();

        Assert.Equal(expected: 2u, guest.PeerId);
        Assert.Equal(expected: 1u, guest.World.HostPeerId);
    }
}

// ── Unit tests — entity ids ───────────────────────────────────────────────────

public sealed class WorldEntityIdTests
{
    [Fact]
    public void NextEntityId_PacksTypeIntoTheUpperBitsAndCountsUp()
    {
        var services = Session.Services();
        var world = new World(Session.Player(services, Session.Registry(), uid: 2001));

        const uint monster = (uint)ProtEntityType.PROT_ENTITY_TYPE_MONSTER << 21;
        const uint gadget = (uint)ProtEntityType.PROT_ENTITY_TYPE_GADGET << 21;

        Assert.Equal(monster | 1, world.NextEntityId(ProtEntityType.PROT_ENTITY_TYPE_MONSTER));
        Assert.Equal(monster | 2, world.NextEntityId(ProtEntityType.PROT_ENTITY_TYPE_MONSTER));
        Assert.Equal(gadget | 3, world.NextEntityId(ProtEntityType.PROT_ENTITY_TYPE_GADGET));
    }

    [Fact]
    public void NextEntityId_SeparateWorlds_CountIndependently()
    {
        var services = Session.Services();
        var registry = Session.Registry();

        var first = new World(Session.Player(services, registry, uid: 2001));
        var second = new World(Session.Player(services, registry, uid: 2002));

        Assert.Equal(first.NextEntityId(ProtEntityType.PROT_ENTITY_TYPE_AVATAR),
            second.NextEntityId(ProtEntityType.PROT_ENTITY_TYPE_AVATAR));
    }
}
