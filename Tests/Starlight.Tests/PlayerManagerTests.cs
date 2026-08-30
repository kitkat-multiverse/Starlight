using Microsoft.Extensions.DependencyInjection;
using Starlight.Game.Modules;
using Starlight.Game.Player;
using Starlight.Protocol;
using Starlight.Rpc.Tunnel;
using Xunit;

namespace Starlight.Tests;

public sealed class PlayerManagerTests
{
    [Fact]
    public void LifecycleEvent_ExistingValuesRemainStable()
    {
        Assert.Equal(expected: 0, (int)LifecycleEvent.PlayerLogin);
        Assert.Equal(expected: 1, (int)LifecycleEvent.PlayerDisconnect);
        Assert.Equal(expected: 2, (int)LifecycleEvent.PlayerSaving);
        Assert.Equal(expected: 3, (int)LifecycleEvent.PlayerBorn);
        Assert.Equal(expected: 4, (int)LifecycleEvent.PlayerTeamChanged);
    }

    [Fact]
    public void Add_IndexesPlayerByUid()
    {
        var manager = new PlayerManager();
        var player = Player(uid: 1001);

        Assert.True(manager.Add(player));
        Assert.True(manager.TryGet(uid: 1001, out var found));
        Assert.Same(player, found);
        Assert.Equal(expected: 1, manager.Count);
    }

    [Fact]
    public void Add_DuplicateUid_LeavesOriginalSessionRegistered()
    {
        var manager = new PlayerManager();
        var original = Player(uid: 1001);
        var duplicate = Player(uid: 1001);

        Assert.True(manager.Add(original));
        Assert.False(manager.Add(duplicate));
        Assert.True(manager.TryGet(uid: 1001, out var found));
        Assert.Same(original, found);
    }

    [Fact]
    public void Remove_OldSession_DoesNotRemoveNewSessionWithSameUid()
    {
        var manager = new PlayerManager();
        var oldSession = Player(uid: 1001);
        var newSession = Player(uid: 1001);

        Assert.True(manager.Add(oldSession));
        Assert.True(manager.Remove(oldSession));
        Assert.True(manager.Add(newSession));

        Assert.False(manager.Remove(oldSession));
        Assert.True(manager.TryGet(uid: 1001, out var found));
        Assert.Same(newSession, found);
    }

    private static StarlightPlayer Player(uint uid)
    {
        var services = new ServiceCollection()
            .AddLogging()
            .BuildServiceProvider();

        var registry = new ModuleRegistry().Build();
        var (_, tunnel) = DirectTunnel.CreatePair();

        return new StarlightPlayer(services, registry, tunnel) { Uid = uid };
    }
}
