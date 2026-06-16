using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Serilog;
using Serilog.Events;

namespace Starlight.Common;

public static class Config
{
    /// Please avoid as much as possible. Use this as a last-resort where
    /// dependency injection falls through.
    public static StarlightConfig Instance { get; private set; } = new();

    public static LogEventLevel LogLevel => Instance.LogLevel;
    public static ExternalResources Resources => Instance.Resources;
    public static ServerConfig Server => Instance.Server;
    public static DatabaseConfig Database => Instance.Database;

    public static void Load(string path = "config.json")
    {
        // Docker containers generally want a read-only system.
        //
        // Usually we use environment variables in that context anyway,
        // so in those cases we forgo writing a JSON file.
        if (!Env.IsContainerized && !File.Exists(path))
        {
            WriteDefaultConfig(path);
        }

        var cfg = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile(path, optional: false, reloadOnChange: true)
            .AddEnvironmentVariables("SL__")
            .Build()
            .Get<StarlightConfig>();

        if (cfg is null)
        {
            Log.Warning("Failed to parse configuration, please check for invalid values!");
            cfg = new StarlightConfig();
        }

        Instance = cfg;
    }

    private static void WriteDefaultConfig(string path)
    {
        var json = JsonSerializer.Serialize(Instance, Constants.JsonOptions);
        File.WriteAllText(path, json);
    }
}

public sealed class StarlightConfig
{
    [JsonConverter(typeof(JsonStringEnumConverter<LogEventLevel>))]
    public LogEventLevel LogLevel { get; set; } = LogEventLevel.Information;
    public ExternalResources Resources { get; set; } = new();
    public ServerConfig Server { get; set; } = new();
    public DatabaseConfig Database { get; set; } = new();
}

public sealed class ExternalResources
{
    public string ResourcesPath { get; set; } = "./resources.zip";
}

public sealed class ServerConfig
{
    public HttpConfig Http { get; set; } = new();
}

public sealed class SqliteConfig
{
    public bool CreateIfMissing { get; set; } = true;
    public bool UseWal { get; set; } = true;
    public string Synchronous { get; set; } = "NORMAL";
    public int BusyTimeoutMilliseconds { get; set; } = 5000;
    public bool AllowClientEvaluation { get; set; } = true;
}

public sealed class DatabaseConfig
{
    public string ConnectionString { get; set; } = "sqlite:./data/starlight.db";
    public SqliteConfig Sqlite { get; set; } = new();
}

public sealed class HttpConfig
{
    public string BindAddress { get; set; } = "0.0.0.0";
    public int BindPort { get; set; } = 8080;
}
