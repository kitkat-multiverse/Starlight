using Serilog;
using Starlight.Commands;

namespace Starlight.Console;

public sealed class ConsoleCommandOutput : ICommandOutput
{
    public ValueTask WriteAsync(CommandOutputLevel level, string message, CancellationToken cancellationToken)
    {
        switch (level)
        {
            case CommandOutputLevel.Information:
                Log.Information("{CommandMessage}", message);
                break;
            case CommandOutputLevel.Warning:
                Log.Warning("{CommandMessage}", message);
                break;
            case CommandOutputLevel.Error:
                Log.Error("{CommandMessage}", message);
                break;
        }

        return ValueTask.CompletedTask;
    }
}
