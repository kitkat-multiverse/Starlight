using Microsoft.Extensions.DependencyInjection;
using Starlight.Commands;

namespace Starlight.Extensions;

public static class ConsoleServiceExtensions
{
    public static IServiceCollection AddConsoleCommands(this IServiceCollection services)
    {
        var commandType = typeof(IConsoleCommand);

        var commands = typeof(IConsoleCommand).Assembly
            .GetTypes()
            .Where(type =>
                commandType.IsAssignableFrom(type) &&
                type is { IsClass: true, IsAbstract: false });

        foreach (var command in commands)
        {
            services.AddSingleton(commandType, command);
        }

        services.AddSingleton<ConsoleCommandRegistry>();
        services.AddHostedService<ConsoleService>();

        return services;
    }
}
