using Microsoft.Extensions.Logging.Abstractions;
using Starlight.Commands;
using Xunit;

namespace Starlight.Tests;

public sealed class CommandDispatcherTests
{
    [Fact]
    public async Task Dispatch_ParsesQuotedArgumentsAndRoutesOutput()
    {
        var command = new RecordingCommand();
        var output = new RecordingOutput();
        var dispatcher = Dispatcher(command);
        var context = new CommandContext(CommandSource.Console, output, CancellationToken.None);

        var result = await dispatcher.DispatchAsync("record alpha \"two words\" 'three words'", context);

        Assert.Equal(CommandDispatchStatus.Executed, result);
        Assert.Equal(["alpha", "two words", "three words"], command.Args);

        Assert.Collection(output.Messages,
            message => Assert.Equal((CommandOutputLevel.Information, "done"), message));
    }

    [Fact]
    public async Task Dispatch_UnknownCommand_ReturnsFeedback()
    {
        var output = new RecordingOutput();
        var dispatcher = Dispatcher(new RecordingCommand());
        var context = new CommandContext(CommandSource.Console, output, CancellationToken.None);

        var result = await dispatcher.DispatchAsync("missing", context);

        Assert.Equal(CommandDispatchStatus.UnknownCommand, result);

        Assert.Collection(output.Messages,
            message => Assert.Equal((CommandOutputLevel.Warning, "Unknown command 'missing'."), message));
    }

    [Fact]
    public async Task Dispatch_PlayerCannotRunConsoleOnlyCommand()
    {
        var command = new RecordingCommand { Sources = CommandSource.Console };
        var output = new RecordingOutput();
        var dispatcher = Dispatcher(command);
        var context = new CommandContext(CommandSource.Player, output, CancellationToken.None);

        var result = await dispatcher.DispatchAsync("record", context);

        Assert.Equal(CommandDispatchStatus.NotAllowed, result);
        Assert.False(command.Executed);

        Assert.Collection(output.Messages,
            message => Assert.Equal((CommandOutputLevel.Warning, "Command 'record' is not available from in-game chat."), message));
    }

    [Fact]
    public async Task Dispatch_UnclosedQuote_DoesNotInvokeCommand()
    {
        var command = new RecordingCommand();
        var output = new RecordingOutput();
        var dispatcher = Dispatcher(command);
        var context = new CommandContext(CommandSource.Console, output, CancellationToken.None);

        var result = await dispatcher.DispatchAsync("record \"broken", context);

        Assert.Equal(CommandDispatchStatus.InvalidInput, result);
        Assert.False(command.Executed);
        Assert.Single(output.Messages);
        Assert.Equal(CommandOutputLevel.Warning, output.Messages[0].Level);
    }

    private static CommandDispatcher Dispatcher(params ICommand[] commands)
        => new(new CommandRegistry(commands), NullLogger<CommandDispatcher>.Instance);

    private sealed class RecordingCommand : ICommand
    {
        public bool Executed { get; private set; }
        public string[] Args { get; private set; } = [];
        public string Name => "record";
        public string Description => "records arguments";
        public string Usage => "record <args>";
        public string[] Aliases => [];
        public CommandSource Sources { get; set; } = CommandSource.All;

        public async Task ExecuteAsync(CommandContext context, string[] args)
        {
            Executed = true;
            Args = args;
            await context.ReplyAsync("done");
        }
    }

    private sealed class RecordingOutput : ICommandOutput
    {
        public List<(CommandOutputLevel Level, string Message)> Messages { get; } = [];

        public ValueTask WriteAsync(CommandOutputLevel level, string message, CancellationToken cancellationToken)
        {
            Messages.Add((level, message));
            return ValueTask.CompletedTask;
        }
    }
}
