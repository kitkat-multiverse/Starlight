using Starlight.Game.Modules;
using IMessage = Starlight.Protobuf.Core.IMessage;

namespace Starlight.Game.Player;

public interface IPlayer
{
    /// <summary>Resolves this player's instance of <typeparamref name="TModule"/>.</summary>
    TModule Module<TModule>() where TModule : class, IModule;

    /// <summary>Sends a message back to the client (out through the gate tunnel).</summary>
    void Send(IMessage message);
}
