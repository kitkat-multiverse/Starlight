using System.IO.Compression;
using Starlight.Common;
using Serilog;

namespace Starlight.Game.Resources;

/// <summary>
/// A helper for reading resources.
/// </summary>
public static class Resources
{
    public static IResourceLoader Loader { get; private set; } = null!;

    /// <summary>
    /// Sets the resource base path.
    /// </summary>
    public static void Initialize()
    {
        var path = Config.Resources.ResourcesPath;

        // Check if the path exists.
        if (!File.Exists(path) && !Directory.Exists(path))
        {
            Log.Error("Resource path does not exist: {Path}", path);
            Environment.Exit(1);
        }

        if (path.EndsWith(".zip"))
        {
            var archive = ZipFile.OpenRead(path);
            Loader = new ZipLoader(archive);
        } else
        {
            var resources = new DirectoryInfo(path);
            Loader = new FolderLoader(resources);
        }
    }
}

internal static class DataExtensions
{
    /// <summary>
    /// Parses the `uint` ID of a resource.
    /// This allows for overriding the serialized name of the ID property.
    /// </summary>
    public static uint GetId<T>(this T resource) where T : Data
    {
        if (resource.GetType().GetProperty("Id") is not {} id)
        {
            throw new Exception($"{typeof(T).Name} is missing an ID property.");
        }

        var value = id.GetValue(resource);

        return value is not uint idValue ?
            throw new Exception($"{typeof(T).Name} ID property is not of type uint.") :
            idValue;
    }
}
