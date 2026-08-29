using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace Starlight.Game.Player;

/// <summary>
/// Players currently logged in to this game server, keyed by their region UID.
/// Persistent player data remains owned by DbGate; this only tracks live sessions.
/// </summary>
public sealed class PlayerManager
{
    private readonly ConcurrentDictionary<uint, IPlayer> _players = [];

    /// <summary>The number of players currently logged in.</summary>
    public int Count => _players.Count;

    /// <summary>Finds the live player with <paramref name="uid"/>.</summary>
    public bool TryGet(uint uid, [MaybeNullWhen(false)] out IPlayer player)
        => _players.TryGetValue(uid, out player);

    /// <summary>
    /// Registers <paramref name="player"/> as online. Returns <c>false</c> when another
    /// live session already owns the same UID.
    /// </summary>
    public bool Add(IPlayer player) => _players.TryAdd(player.Uid, player);

    /// <summary>
    /// Removes this exact player session. Matching the instance as well as the UID prevents
    /// a delayed disconnect from removing a newer session for the same player.
    /// </summary>
    public bool Remove(IPlayer player)
        => ((ICollection<KeyValuePair<uint, IPlayer>>)_players)
            .Remove(new KeyValuePair<uint, IPlayer>(player.Uid, player));

    /// <summary>Returns a stable snapshot for commands and broadcasts to enumerate.</summary>
    public IReadOnlyList<IPlayer> Snapshot() => [.. _players.Values];
}
