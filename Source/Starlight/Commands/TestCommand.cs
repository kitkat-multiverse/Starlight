namespace Starlight.Commands;

public sealed class TestCommand : ICommand
{
    public string Name => "test";
    public string Description => "Test command";
    public string[] Aliases => [];
    public string Usage => "test";

    public async Task ExecuteAsync(CommandContext context, string[] args)
        => await context.ReplyAsync("Starlight is running! This is a test command.");
}
