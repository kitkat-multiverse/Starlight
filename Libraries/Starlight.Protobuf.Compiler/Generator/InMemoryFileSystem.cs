using System.Collections.Generic;
using System.IO;
using Google.Protobuf.Reflection;

namespace Starlight.Protobuf.Compiler;

internal sealed class InMemoryFileSystem(IReadOnlyDictionary<string, string> sources) : IFileSystem
{
    public bool Exists(string path) => sources.ContainsKey(Normalize(path));

    public TextReader? OpenText(string path) =>
        sources.TryGetValue(Normalize(path), out var content) ? new StringReader(content) : null;

    private static string Normalize(string path) => Path.GetFileName(path);
}
