using Microsoft.Extensions.DependencyInjection;
using Starlight.Commands;

namespace Starlight.Console;

public static class ConsoleServiceExtensions
{
    public static IServiceCollection AddCommands(this IServiceCollection services)
    {
        var commandType = typeof(ICommand);

        var commands = typeof(ICommand).Assembly
            .GetTypes()
            .Where(type =>
                commandType.IsAssignableFrom(type) &&
                type is { IsClass: true, IsAbstract: false });

        foreach (var command in commands)
        {
            services.AddSingleton(commandType, command);
        }

        services
            .AddSingleton<CommandRegistry>()
            .AddSingleton<CommandDispatcher>()
            .AddSingleton<ConsoleCommandOutput>()
            .AddHostedService<ConsoleService>();

        return services;
    }
}
