using Starlight.Game.Player;

namespace Starlight.Commands;

public sealed record CommandContext(
    CommandSource Source,
    ICommandOutput Output,
    CancellationToken CancellationToken,
    IPlayer? Invoker = null,
    IPlayer? Target = null
)
{
    public ValueTask ReplyAsync(string message, CommandOutputLevel level = CommandOutputLevel.Information)
        => Output.WriteAsync(level, message, CancellationToken);
}
