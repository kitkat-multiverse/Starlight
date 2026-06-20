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
        // Without an interactive console (systemd/Docker/redirected stdin) there is
        // nothing to read, and a blocking read here would stall graceful shutdown.
        if (System.Console.IsInputRedirected)
            return;

        while (!stoppingToken.IsCancellationRequested)
        {
            System.Console.Write("> ");

            var input = await ReadLineAsync(stoppingToken);

            // Cancellation requested (shutdown) or end of input stream.
            if (input is null)
                break;

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

    /// <summary>
    /// Reads a line from the console without leaving graceful shutdown blocked.
    /// <see cref="System.Console.In"/> ignores cancellation once the underlying
    /// blocking read has started, so the read runs on a separate thread and is
    /// abandoned when cancellation is requested.
    /// </summary>
    private static async Task<string?> ReadLineAsync(CancellationToken cancellationToken)
    {
        var readTask = Task.Run(System.Console.ReadLine, CancellationToken.None);

        var cancelled = new TaskCompletionSource();

        await using (cancellationToken.Register(() => cancelled.TrySetResult()))
        {
            if (await Task.WhenAny(readTask, cancelled.Task) != readTask)
                return null;
        }

        return await readTask;
    }
}
