namespace Starlight.Common;

public static class RandomExtensions
{
    /// <summary>
    /// Generates a random version 4 UUID.
    /// </summary>
    public static Guid NextUuid(this Random random) {
        Span<byte> bytes = stackalloc byte[16];
        random.NextBytes(bytes);

        bytes[6] = (byte)(bytes[6] & 0x0F | 0x40); // version 4
        bytes[8] = (byte)(bytes[8] & 0x3F | 0x80); // variant 1 (RFC 4122)

        return new Guid(bytes);
    }
}

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
