using Starlight.Kcp;
using IMessage = Starlight.Protobuf.Core.IMessage;

namespace Starlight.Game;

/// <summary>
/// Thrown by any handler to abort the current packet's handler chain and disconnect the player.
/// Unwinding stops the remaining handlers; the player then sends <see cref="Replies"/> and asks
/// the gate to drop the client with <see cref="Reason"/>. When <see cref="Flush"/> is set the gate
/// waits for those replies to be acknowledged before tearing the connection down.
/// </summary>
public sealed class KickException(uint reason, bool flush, params IMessage[] replies) : Exception
{
    public uint Reason { get; } = reason;
    public bool Flush { get; } = flush;
    public IReadOnlyList<IMessage> Replies { get; } = replies;

    public KickException(DisconnectReason reason, params IMessage[] replies) : this((uint)reason, flush: true, replies)
    { }
}
