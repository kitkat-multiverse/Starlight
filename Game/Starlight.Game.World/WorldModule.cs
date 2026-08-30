using Starlight.Game.Modules;
using Starlight.Game.Player;
using Starlight.Protocol;
using Starlight.Rpc.Proto;

namespace Starlight.Game.World;

/// <summary>
/// Tracks which world and scene a player is currently in.<br/>
/// <see cref="World"/> is swapped out whenever they join or leave co-op, so hold onto this module
/// and read through it rather than caching the world itself.
/// </summary>
public sealed class WorldModule(IPlayer player, WorldManager worlds) : IModule
{
    private World? _world;

    /// The world this player is in. Only valid once they have entered one.
    public World World => _world!;

    /// This player's ID inside <see cref="World"/>, or 0 before they enter one.
    public uint PeerId => _world?.PeerIdOf(player) ?? 0;

    /// The scene this player is standing in, or null until their first scene load.
    public Scene? Scene { get; internal set; }

    [Lifecycle(LifecycleEvent.PlayerLogin)]
    public void OnLogin()
    {
        if (player.State.BornState != NetPlayerState.Types.PlayerBornState.Pending)
            EnterOwnWorld();
    }

    /// <summary>Puts this player into their own world. Call once login has assigned their uid.</summary>
    public void EnterOwnWorld() => Enter(worlds.Open(player));

    /// <summary>Moves this player into <paramref name="world"/>, leaving whichever one they were in.</summary>
    public void Enter(World world)
    {
        if (_world is not null)
            worlds.Leave(_world, player);

        _world = world;
        world.Join(player);
        Scene = null;
    }
}
