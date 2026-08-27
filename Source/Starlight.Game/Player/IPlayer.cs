using Starlight.Game.Modules;
using IMessage = Starlight.Protobuf.Core.IMessage;

namespace Starlight.Game.Player;

public interface IPlayer
{
    uint Uid { get; internal set; }

    /// <summary>Resolves this player's instance of <typeparamref name="TModule"/>.</summary>
    TModule Module<TModule>() where TModule : class, IModule;

    /// <summary>
    /// Sends a message back to the client (out through the gate tunnel). Await it to keep
    /// ordering against whatever you send next, or <c>Defer()</c> it if you don't care.
    /// </summary>
    Task Send(IMessage message);
}
