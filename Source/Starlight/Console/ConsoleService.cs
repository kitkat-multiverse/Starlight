using Microsoft.Extensions.Hosting;
using Starlight.Commands;

namespace Starlight.Console;

public sealed class ConsoleService(
    CommandDispatcher dispatcher,
    ConsoleCommandOutput output,
    InteractiveConsole console
) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (System.Console.IsInputRedirected)
            return;

        var context = new CommandContext(CommandSource.Console, output, stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            var input = await console.ReadLineAsync(stoppingToken);

            if (input is null)
                break;

            await dispatcher.DispatchAsync(input, context);
        }
    }
}
