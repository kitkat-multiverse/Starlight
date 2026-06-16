using System.Diagnostics;
using Starlight.Common.Config;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Sinks.SystemConsole.Themes;
using Starlight.Console;
using Starlight.Database.DependencyInjection;
using Starlight.Game;
using Starlight.Game.Resources;
using Starlight.SDK;

namespace Starlight;

internal static class Program
{
    private static readonly Stopwatch StartTime = Stopwatch.StartNew();

    #region Logger

    public static readonly LoggingLevelSwitch 
        LogLevel = new(), 
        HttpLogLevel = new(LogEventLevel.Warning);
    
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
            .MinimumLevel.Override("Microsoft.AspNetCore", HttpLogLevel)
            .WriteTo.Console(
                outputTemplate: LoggerConsoleTemplate,
                theme: LoggerTheme)
            .WriteTo.File(
                path: "logs/latest.log",
                rollingInterval: RollingInterval.Day,
                outputTemplate: LoggerFileTemplate,
                restrictedToMinimumLevel: LogEventLevel.Information)
            .CreateLogger();
        Log.Information("Starting Starlight...");
        
        Config.Load();
        LogLevel.MinimumLevel = Config.LogLevel;
        
        try
        {
            var builder = Host.CreateApplicationBuilder(args);

            builder.Services
                .AddSerilog()
                .AddSingleton(_ => Config.Instance)
                .AddSingleton<GameData>()
                .AddStarlightDatabase(Config.Instance, typeof(GateServerService).Assembly, typeof(Program).Assembly);

            builder.Services
                .AddCommands()
                .AddHostedService<GateServerService>()
                .AddHostedService<HttpServerService>();

            // Prepare the application.
            var app = builder.Build();

            StartTime.Stop();
            Log.Information("Done! Finished starting in {Elapsed}ms.", StartTime.ElapsedMilliseconds);

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
