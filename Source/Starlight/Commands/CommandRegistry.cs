using Starlight.Commands;

namespace Starlight;

public sealed class CommandRegistry(IEnumerable<ICommand> commands)
{
    private readonly Dictionary<string, ICommand> _commands = 
        commands
            .SelectMany(command => 
                new[] { command.Name }
                    .Concat(command.Aliases)
                    .Select(name => new { name, command }))
            .ToDictionary(x => x.name.ToLowerInvariant(), x => x.command);

    public IReadOnlyCollection<ICommand> Commands => _commands.Values.Distinct().ToArray();

    public bool TryGet(string name, out ICommand command)
        => _commands.TryGetValue(name, out command!);
}
