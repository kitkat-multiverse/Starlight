using Starlight.Game.Modules;
using Starlight.Protocol;
using Starlight.Rpc.Proto;
using IMessage = Starlight.Protobuf.Core.IMessage;

namespace Starlight.Game.Player;

public interface IPlayer
{
    uint Uid { get; internal set; }

    /// The SDK account behind this player. Set from the gate's connect notify, since the
    /// account uid on PlayerLoginReq comes through empty.
    string AccountUid { get; internal set; }

    /// <summary>Fires when the player's gate tunnel closes.</summary>
    CancellationToken Closing { get; }

    /// <summary>Game-owned state loaded from and saved through DbGate.</summary>
    NetPlayerState State { get; internal set; }

    /// <summary>Synchronizes access to this player's live module and persisted state.</summary>
    object StateLock { get; }

    /// <summary>Resolves this player's instance of <typeparamref name="TModule"/>.</summary>
    TModule Module<TModule>() where TModule : class, IModule;

    /// <summary>
    /// Sends a message back to the client (out through the gate tunnel). Await it to order it
    /// against the next send, or <c>Defer()</c> it.
    /// </summary>
    Task Send(IMessage message);

    /// <summary>
    /// Runs every <c>[Lifecycle]</c> handler for <paramref name="event"/>, in no particular order.
    /// Faults propagate, so a <see cref="Starlight.Kcp.KickException"/> from a handler still kicks.
    /// </summary>
    ValueTask Emit(LifecycleEvent @event);
}
