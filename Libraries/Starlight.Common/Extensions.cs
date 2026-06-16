namespace Starlight.Common;

public static class StringExtensions
{
    /// <summary>
    /// Returns the file extension of a file name.
    /// </summary>
    public static string FileExtension(this string fileName) {
        var index = fileName.LastIndexOf('.');
        return index == -1 ? string.Empty : fileName[(index + 1)..];
    }
}
