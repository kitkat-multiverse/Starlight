using Starlight.Game.Ability;
using Starlight.Game.Player;
using Starlight.Protocol;

namespace Starlight.Game.World;

public sealed class World
{
    private const uint OwnerPeerId = 1;

    private readonly Dictionary<uint, Scene> _scenes = [];
    private readonly Dictionary<uint, IPlayer> _peers = [];
    private readonly Dictionary<IPlayer, uint> _peerIds = [];
    private readonly Dictionary<IPlayer, uint> _teamEntityIds = [];

    private uint _nextEntityId;
    private uint _levelEntityId;

    public World(IPlayer owner)
    {
        Owner = owner;
    }

    public IPlayer Owner { get; }
    public IReadOnlyDictionary<uint, IPlayer> Peers => _peers;
    public IReadOnlyDictionary<uint, Scene> Scenes => _scenes;
    public AbilityScope Abilities { get; } = new();
    public uint HostPeerId => PeerIdOf(Owner);
    public uint LevelEntityId =>
        _levelEntityId == 0 ? _levelEntityId = NextEntityId(ProtEntityType.PROT_ENTITY_TYPE_MP_LEVEL) : _levelEntityId;

    public uint PeerIdOf(IPlayer player) => _peerIds.GetValueOrDefault(player);

    public uint TeamEntityIdOf(IPlayer player)
    {
        if (!_teamEntityIds.TryGetValue(player, out var entityId))
            _teamEntityIds[player] = entityId = NextEntityId(ProtEntityType.PROT_ENTITY_TYPE_TEAM);
        return entityId;
    }

    public uint Join(IPlayer player)
    {
        Leave(player);

        var peerId = player == Owner ? OwnerPeerId : NextFreePeerId();

        if (_peers.TryGetValue(peerId, out var displaced))
            Seat(displaced, NextFreePeerId());

        Seat(player, peerId);
        return peerId;
    }

    public void Leave(IPlayer player)
    {
        if (_peerIds.Remove(player, out var peerId))
            _peers.Remove(peerId);

        if (_teamEntityIds.Remove(player, out var teamEntityId))
            Abilities.Remove(teamEntityId);

        if (player.Uid != 0)
            Abilities.RemoveOwnedByPlayer(player.Uid);
    }

    public Scene GetScene(uint sceneId)
    {
        if (!_scenes.TryGetValue(sceneId, out var scene))
            _scenes[sceneId] = scene = new Scene(this, sceneId);

        return scene;
    }

    public uint NextEntityId(ProtEntityType type) => (uint)type << 21 | ++_nextEntityId & 0xFFFFFF;

    private void Seat(IPlayer player, uint peerId)
    {
        _peers[peerId] = player;
        _peerIds[player] = peerId;
    }

    private uint NextFreePeerId()
    {
        var peerId = OwnerPeerId;

        while (_peers.ContainsKey(peerId))
            peerId++;
        return peerId;
    }
}
