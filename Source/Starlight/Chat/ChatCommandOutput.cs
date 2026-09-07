using Starlight.Commands;
using Starlight.Game.Player;

namespace Starlight.Chat;

internal sealed class ChatCommandOutput(ChatService chat, IPlayer player) : ICommandOutput
{
    public async ValueTask WriteAsync(
        CommandOutputLevel level,
        string message,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        await chat.SendServerMessageAsync(player, message);
    }
}
