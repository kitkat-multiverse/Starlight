using System.Text;
using Microsoft.Extensions.Logging;

namespace Starlight.Commands;

public enum CommandDispatchStatus
{
    Empty,
    Executed,
    UnknownCommand,
    NotAllowed,
    InvalidInput,
    Failed
}

public sealed class CommandDispatcher(
    CommandRegistry registry,
    ILogger<CommandDispatcher> logger
)
{
    public async ValueTask<CommandDispatchStatus> DispatchAsync(string input, CommandContext context)
    {
        if (string.IsNullOrWhiteSpace(input))
            return CommandDispatchStatus.Empty;

        if (!TryTokenize(input, out var parts, out var error))
        {
            await context.ReplyAsync(error!, CommandOutputLevel.Warning);
            return CommandDispatchStatus.InvalidInput;
        }

        if (parts.Length == 0)
            return CommandDispatchStatus.Empty;

        var name = parts[0];

        if (!registry.TryGet(name, out var command))
        {
            await context.ReplyAsync($"Unknown command '{name}'.", CommandOutputLevel.Warning);
            return CommandDispatchStatus.UnknownCommand;
        }

        if ((command.Sources & context.Source) == 0)
        {
            var source = context.Source == CommandSource.Player ? "in-game chat" : "the console";

            await context.ReplyAsync(
                $"Command '{command.Name}' is not available from {source}.",
                CommandOutputLevel.Warning);
            return CommandDispatchStatus.NotAllowed;
        }

        try
        {
            await command.ExecuteAsync(context, parts[1..]);
            return CommandDispatchStatus.Executed;
        }
        catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Command failed: {Command}", command.Name);

            await context.ReplyAsync(
                $"Command '{command.Name}' failed: {exception.Message}",
                CommandOutputLevel.Error);
            return CommandDispatchStatus.Failed;
        }
    }

    internal static bool TryTokenize(string input, out string[] parts, out string? error)
    {
        var tokens = new List<string>();
        var current = new StringBuilder();
        var quote = '\0';
        var tokenStarted = false;

        for (var index = 0; index < input.Length; index++)
        {
            var character = input[index];

            if (character == '\\')
            {
                if (index + 1 < input.Length)
                {
                    var next = input[index + 1];

                    var canEscape = next == '\\'
                                    || next is '\'' or '"'
                                    || quote == '\0' && char.IsWhiteSpace(next);

                    if (canEscape)
                    {
                        current.Append(next);
                        tokenStarted = true;
                        index++;
                        continue;
                    }
                }

                current.Append(character);
                tokenStarted = true;
                continue;
            }

            if (quote != '\0')
            {
                if (character == quote)
                    quote = '\0';
                else
                    current.Append(character);

                continue;
            }

            if (character is '\'' or '"')
            {
                quote = character;
                tokenStarted = true;
                continue;
            }

            if (char.IsWhiteSpace(character))
            {
                if (!tokenStarted)
                    continue;

                tokens.Add(current.ToString());
                current.Clear();
                tokenStarted = false;
                continue;
            }

            current.Append(character);
            tokenStarted = true;
        }

        if (quote != '\0')
        {
            parts = [];
            error = "Command contains an unclosed quote.";
            return false;
        }

        if (tokenStarted)
            tokens.Add(current.ToString());

        parts = [.. tokens];
        error = null;
        return true;
    }
}
