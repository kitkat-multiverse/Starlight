using Starlight.Game.Player;
using System.Diagnostics.CodeAnalysis;

namespace Starlight.Game.World;

/// <summary>
/// Every world open on this game server, keyed by the uid of the player who owns it.<br/>
/// The key is a uid rather than anything server-local because co-op joins arrive from the gate,
/// which only ever identifies a host by uid.
/// </summary>
public sealed class WorldManager
{
    private readonly Dictionary<uint, World> _worlds = [];

    /// <summary>Returns <paramref name="owner"/>'s world, opening one if they don't have it yet.</summary>
    public World Open(IPlayer owner)
    {
        if (!_worlds.TryGetValue(owner.Uid, out var world))
            _worlds[owner.Uid] = world = new World(owner);

        return world;
    }

    /// <summary>Finds the world hosted by <paramref name="hostUid"/>, for a player joining co-op.</summary>
    public bool TryGet(uint hostUid, [MaybeNullWhen(false)] out World world)
        => _worlds.TryGetValue(hostUid, out world);

    /// <summary>Drops <paramref name="player"/> from <paramref name="world"/>, closing it once the last one leaves.</summary>
    public void Leave(World world, IPlayer player)
    {
        world.Leave(player);

        if (world.Peers.Count == 0)
            _worlds.Remove(world.Owner.Uid);
    }
}
