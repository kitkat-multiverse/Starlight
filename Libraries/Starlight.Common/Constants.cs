using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Starlight.Common;

public static class Constants
{
    public static readonly JsonSerializerOptions JsonOptions = new() {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.PascalCase) },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}

public static class Env
{
    /// If true, the application is running in a container.
    public static bool IsContainerized => GetOrDefault("DOTNET_RUNNING_IN_CONTAINER", out _);

    /// <summary>
    /// Fetches an environment variable or returns false if it is not set or empty.
    /// </summary>
    public static bool GetOrDefault(string variable, out string value)
    {
        var env = Environment.GetEnvironmentVariable(variable);

        if (string.IsNullOrWhiteSpace(env))
        {
            value = string.Empty;
            return false;
        }

        value = env;
        return true;
    }
}
