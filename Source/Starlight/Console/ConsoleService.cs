using Microsoft.Extensions.Hosting;
using Serilog;

namespace Starlight.Console;

public sealed class ConsoleService(
    CommandRegistry registry,
    IHostApplicationLifetime lifetime
) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            System.Console.Write("> ");

            var input = await System.Console.In.ReadLineAsync(stoppingToken);

            if (string.IsNullOrWhiteSpace(input))
                continue;

            var parts = input.Split(separator: ' ', StringSplitOptions.RemoveEmptyEntries);
            var name = parts[0];
            var args = parts[1..];

            if (!registry.TryGet(name, out var command))
            {
                Log.Warning("Unknown command: {Command}", name);
                continue;
            }

            try
            {
                await command.ExecuteAsync(args, stoppingToken);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Command failed: {Command}", name);
            }
        }
    }
}
