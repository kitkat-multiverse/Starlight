using System.Text.Json;
using Serilog.Events;
using Starlight.Common;
using Starlight.DbGate;
using Starlight.Gate;
using Starlight.SDK;

// ReSharper disable UnusedType.Global
// ReSharper disable UnusedMember.Global

namespace Starlight;

/// <summary>
/// A combined configuration class for the Starlight all-in-one launcher.
/// <br/>
/// Developers who launch each service individually; feel free to do as you please.
/// </summary>
public sealed class Config
{
    public LogEventLevel LogLevel { get; set; } = LogEventLevel.Information;
    public GateConfig Gate { get; set; } = new();
    public DbGateConfig DbGate { get; set; } = new();
    public DispatchConfig Dispatch { get; set; } = new();
    public SdkConfig Sdk { get; set; } = new();

    public static void SaveDefaultConfig()
    {
        // In Docker containers, we want to preserve our 'read-only' system,
        // so we skip generating the configuration in these cases.
        if (Env.IsContainerized || File.Exists("config.json")) return;

        // Add listed defaults.
        //
        // `Microsoft.Extensions.Configuration` will try to append to
        // any pre-defined lists if we don't do this.
        var defaults = new Config();
        defaults.Dispatch.Regions.Add(new DispatchRegionConfig());
        defaults.Dispatch.ClientCustomConfig.CodeSwitch.AddRange([3201, 3237, 3248, 3628, 4334]);
        defaults.Dispatch.ClientCustomConfig.CoverSwitch.Add(40);

        var config = JsonSerializer.Serialize(defaults, Constants.JsonOptions);
        File.WriteAllText("config.json", config);
    }
}
