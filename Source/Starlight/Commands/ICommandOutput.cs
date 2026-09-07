namespace Starlight.Commands;

public enum CommandOutputLevel
{
    Information,
    Warning,
    Error
}

public interface ICommandOutput
{
    ValueTask WriteAsync(CommandOutputLevel level, string message, CancellationToken cancellationToken);
}

internal sealed class NullCommandOutput : ICommandOutput
{
    public static NullCommandOutput Instance { get; } = new();

    public ValueTask WriteAsync(CommandOutputLevel level, string message, CancellationToken cancellationToken)
        => ValueTask.CompletedTask;
}
