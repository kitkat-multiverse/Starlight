namespace Starlight.Commands;

public interface IConsoleCommand
{
    string Name { get; }
    string Description { get; }
    string Usage  { get; }
    string[] Aliases { get; }

    Task ExecuteAsync(string[] args, CancellationToken cancellationToken);
}
