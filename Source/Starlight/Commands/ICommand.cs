namespace Starlight.Commands;

[Flags]
public enum CommandSource
{
    None = 0,
    Console = 1,
    Player = 2,
    All = Console | Player
}

public interface ICommand
{
    string Name { get; }
    string Description { get; }
    string Usage { get; }
    string[] Aliases { get; }
    CommandSource Sources => CommandSource.All;

    Task ExecuteAsync(CommandContext context, string[] args);
}

public static class CommandExtensions
{
    public static Task ExecuteAsync(this ICommand command, string[] args, CancellationToken cancellationToken)
        => command.ExecuteAsync(
            new CommandContext(CommandSource.Console, NullCommandOutput.Instance, cancellationToken),
            args);
}
