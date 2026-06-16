using Serilog;

namespace Starlight.Commands;

public class TestCommand : ICommand
{
    public string Name => "test";
    public string Description => "Test command";
    public string[] Aliases => new string[] {};
    public string Usage => "test";

    public Task ExecuteAsync(string[] args, CancellationToken cancellationToken)
    {
        Log.Information("Starlight is running! This is a test command.");
        return Task.CompletedTask;
    }
}
