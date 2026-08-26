using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Sinks.SystemConsole.Themes;
using Starlight.DbGate;
using Starlight.Console;
using Starlight.Crypto.Client;
using Starlight.Game;
using Starlight.Game.Modules;
using Starlight.Protocol.V66;
using Starlight.Gate;
using Starlight.Game.Resources;
using Starlight.Rpc;
using Starlight.Rpc.Tunnel;
using Starlight.Rpc.Tunnel.Connection;
using Starlight.SDK;

namespace Starlight;

internal static class Program
{
    private static readonly Stopwatch StartTime = Stopwatch.StartNew();

    #region Logger

    public static readonly LoggingLevelSwitch
        LogLevel = new(),
        VerboseLogLevel = new(LogEventLevel.Warning);

    private const string LoggerConsoleTemplate = "{Timestamp:yyyy-MM-dd HH:mm:ss} « {Level:u3} » {Message:lj}{NewLine}{Exception}";
    private const string LoggerFileTemplate = "{Timestamp:yyyy-MM-dd HH:mm:ss} « {Level:u3} » {Message:lj}{NewLine}";

    private static readonly AnsiConsoleTheme LoggerTheme = new(new Dictionary<ConsoleThemeStyle, string> {
        [ConsoleThemeStyle.Text] = "\e[38;5;0015m",
        [ConsoleThemeStyle.SecondaryText] = "\e[38;5;0007m",
        [ConsoleThemeStyle.TertiaryText] = "\e[38;5;0008m",
        [ConsoleThemeStyle.Invalid] = "\e[38;5;0011m",
        [ConsoleThemeStyle.Null] = "\e[38;5;0027m",
        [ConsoleThemeStyle.Name] = "\e[38;5;0007m",
        [ConsoleThemeStyle.String] = "\e[38;5;0045m",
        [ConsoleThemeStyle.Number] = "\e[38;2;255;165;0m",
        [ConsoleThemeStyle.Boolean] = "\e[38;5;0027m",
        [ConsoleThemeStyle.Scalar] = "\e[38;5;0085m",
        [ConsoleThemeStyle.LevelVerbose] = "\e[38;5;0007m",
        [ConsoleThemeStyle.LevelDebug] = "\e[38;5;218m",
        [ConsoleThemeStyle.LevelInformation] = "\e[38;5;120m",
        [ConsoleThemeStyle.LevelWarning] = "\e[38;5;216m",
        [ConsoleThemeStyle.LevelError] = "\e[38;5;210m",
        [ConsoleThemeStyle.LevelFatal] = "\e[38;5;0015m\e[48;5;0196m"
    });

    #endregion

    /// <summary>
    /// Console entry point.
    /// </summary>
    /// <param name="args">Command line arguments.</param>
    private static async Task<int> Main(string[] args)
    {
        Log.Logger = new LoggerConfiguration()
            .Enrich.FromLogContext()
            .MinimumLevel.ControlledBy(LogLevel)
            .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.Extensions.Hosting", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.AspNetCore", VerboseLogLevel)
            .MinimumLevel.Override("Microsoft.EntityFrameworkCore", VerboseLogLevel)
            .WriteTo.Console(
                outputTemplate: LoggerConsoleTemplate,
                theme: LoggerTheme)
            .WriteTo.File(
                "logs/latest.log",
                rollingInterval: RollingInterval.Day,
                outputTemplate: LoggerFileTemplate)
            .CreateLogger();
        Log.Information("Starting Starlight...");

        try
        {
            Config.SaveDefaultConfig();

            var builder = WebApplication.CreateBuilder(args);

            builder.Configuration
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("config.json", optional: true, reloadOnChange: true)
                .AddEnvironmentVariables("SL__");

            LogLevel.MinimumLevel = builder.Configuration
                .GetValue("LogLevel", LogEventLevel.Information);

            var moduleRegistry = new ModuleRegistry()
                .AddGameComponent()
                .Build();

            builder
                // Add server services.
                // These are all the servers we run in our Starlight launcher.
                .AddSdkServer()
                .AddDispatchServer()
                .AddDbGate()
                .AddGateServer(new V66ProtocolRegistry())
                .AddGameServer(moduleRegistry)
                // Add dependency services.
                // The server services use these to operate.
                .Services
                .AddSerilog()
                .AddCommands()
                .AddSingleton<GameData>()
                .AddHostedService(s => s.GetRequiredService<GameData>())
                // Client crypto contains the RSA keys used in dispatch, gate, & on the client.
                .AddSingleton(_ => ClientCrypto.Create(builder.GetClientCryptoOptions()))
                // RPC Tunnel: Used for connecting the gate & game servers.
                .AddSingleton<ITunnelBroker, DirectTunnelBroker>()
                .AddSingleton<ITunnelConnector, DirectTunnelConnector>()
                .AddSingleton<ITunnelAcceptor, DirectTunnelAcceptor>()
                .AddSingleton<TunnelClient>()
                .AddSingleton<TunnelHost>()
                // RPC: Used for sending messages between services.
                .AddSingleton<RpcTransport, DirectRpcTransport>()
                .AddHostedService(s => s.GetRequiredService<RpcTransport>());

            // Prepare the application.
            var app = builder.Build();

            // Map HTTP endpoints (SDK & dispatch).
#if DEBUG
            app.UseSerilogRequestLogging();
#endif
            app
                .MapSdkServer()
                .MapDispatchServer();

            StartTime.Stop();
            Log.Information("Finished initializing in {Elapsed}ms.", StartTime.ElapsedMilliseconds);

            await app.RunAsync();
            return 0;
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Failed to start application");
            return 1;
        }
        finally
        {
            await Log.CloseAndFlushAsync();
        }
    }
}
