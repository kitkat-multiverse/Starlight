using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Starlight.Common;
using Serilog;

namespace Starlight.Game.Resources;

public interface IResourceLoader
{
    /// <summary>
    /// Lists all files in a directory.
    /// </summary>
    /// <param name="path">The path to the directory, relative to its base.</param>
    /// <param name="searchPattern">The pattern of files to search for.</param>
    /// <returns>A list of file paths, relative to its base.</returns>
    string[] ListFiles(string path, string searchPattern = "*");

    /// <summary>
    /// Reads the raw binary data of a resource.
    /// </summary>
    /// <param name="path">The path to the resource, relative to its base.</param>
    /// <returns>The resource's binary data.</returns>
    byte[] ReadRaw(string path);
}

internal static class ResourceLoaderExtensions
{
    /// <summary>
    /// Reads a JSON file and deserializes it into an object.
    /// </summary>
    /// <param name="loader">The resource loader.</param>
    /// <param name="path">The relative path to the resource.</param>
    /// <typeparam name="T">The type to deserialize the data as.</typeparam>
    public static T? ReadJson<T>(this IResourceLoader loader, string path)
    {
        try
        {
            var data = Encoding.UTF8.GetString(loader.ReadRaw(path));
            return JsonSerializer.Deserialize<T>(data, Constants.JsonOptions);
        }
        catch (Exception ex)
        {
            Log.Debug("Failed to read JSON from {Path}: {ex}", path, ex);
            return default;
        }
    }

    /// <summary>
    /// Reads a JSON file and deserializes it into an object.
    /// </summary>
    /// <param name="loader">The resource loader.</param>
    /// <param name="path">The relative path to the resource.</param>
    /// <param name="type">The type to deserialize the data as.</param>
    public static object? ReadJson(this IResourceLoader loader, string path, Type type)
    {
        try
        {
            var data = Encoding.UTF8.GetString(loader.ReadRaw(path));
            return JsonSerializer.Deserialize(data, type, Constants.JsonOptions);
        }
        catch (Exception exception)
        {
            Log.Debug("Failed to read JSON from {Path}: {Exception}", path, exception);
            return null;
        }
    }
}

public class FolderLoader(DirectoryInfo resources) : IResourceLoader
{
    public string[] ListFiles(string path, string searchPattern = "*") =>
        Directory.GetFiles(Path.Combine(resources.FullName, path), searchPattern);

    public byte[] ReadRaw(string path) => File.ReadAllBytes(Path.Combine(resources.FullName, path));
}

public class ZipLoader(ZipArchive archive) : IResourceLoader
{
    public string[] ListFiles(string path, string searchPattern = "*")
    {
        var regexPattern = "^" + Regex.Escape(searchPattern)
            .Replace(@"\*", ".*")
            .Replace(@"\?", ".") + "$";
        var regex = new Regex(regexPattern, RegexOptions.IgnoreCase);

        lock (archive)
        {
            return archive.Entries
                .Where(e => e.FullName.StartsWith(path) &&
                            regex.IsMatch(Path.GetFileName(e.FullName)))
                .Select(e => e.FullName)
                .ToArray();
        }
    }

    public byte[] ReadRaw(string path)
    {
        lock (archive)
        {
            var entry = archive.GetEntry(path);
            if (entry == null) throw new Exception("File does not exist.");

            using var stream = entry.Open();
            using var reader = new BinaryReader(stream);
            return reader.ReadBytes((int)entry.Length);
        }
    }
}
