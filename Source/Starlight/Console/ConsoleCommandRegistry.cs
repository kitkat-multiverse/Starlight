using Starlight.Commands;

namespace Starlight;

public sealed class ConsoleCommandRegistry(IEnumerable<IConsoleCommand> commands)
{
    private readonly Dictionary<string, IConsoleCommand> _commands = 
        commands
            .SelectMany(command => 
                new[] { command.Name }
                    .Concat(command.Aliases)
                    .Select(name => new { name, command }))
            .ToDictionary(x => x.name.ToLowerInvariant(), x => x.command);

    public IReadOnlyCollection<IConsoleCommand> Commands => _commands.Values.Distinct().ToArray();

    public bool TryGet(string name, out IConsoleCommand command)
    {
        return _commands.TryGetValue(name, out command!);
    }
}
