using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Google.Protobuf.Reflection;
using Microsoft.CodeAnalysis;

namespace Starlight.Protobuf.Compiler;

public sealed partial class ProtobufCompiler
{
    private static FileDescriptorSet Parse(SourceProductionContext ctx, IEnumerable<Proto> group, IEnumerable<Proto> allFiles)
    {
        // The filesystem exposes every proto so `import` directives resolve, even
        // imports that point at files outside the parsed group (e.g. extra.proto's
        // custom-option extensions). Only the group is added to output, however.
        var fsSources = new Dictionary<string, string>();

        foreach (var p in allFiles)
        {
            fsSources[p.FileName] = p.Content;
        }

        var groupList = group.ToList();

        foreach (var p in groupList)
        {
            fsSources[p.FileName] = p.Content;
        }

        var set = new FileDescriptorSet { FileSystem = new InMemoryFileSystem(fsSources) };
        // protobuf-net only searches its importPaths for unresolved imports; with none
        // registered the FileSystem is never consulted. "." is enough because
        // InMemoryFileSystem normalizes every lookup to its file name.
        set.AddImportPath(".");

        foreach (var p in groupList)
        {
            set.Add(p.FileName, includeInOutput: true, new StringReader(p.Content));
        }

        set.Process();

        // The transform options (add/xor/fop/mask) are extensions whose *values* we
        // read by text-scanning, not through protobuf-net's option resolution. The
        // parser still flags them as unresolved custom options, but the field's
        // structure (type/name/number) is intact, so those specific errors are noise.
        foreach (var error in set.GetErrors().Where(e => e.IsError && !IsTransformOptionError(e.Message)))
        {
            ctx.ReportDiagnostic(Diagnostic.Create(
                ParseError, Location.None, error.File, error.LineNumber, error.ColumnNumber, error.Message));
        }

        return set;
    }

    private static readonly string[] TransformOptionNames = ["add", "xor", "fop", "mask", "alts"];

    private static bool IsTransformOptionError(string message)
    {
        if (message.IndexOf("custom option", System.StringComparison.OrdinalIgnoreCase) < 0)
            return false;

        foreach (var name in TransformOptionNames)
        {
            if (message.IndexOf($"'{name}'", System.StringComparison.Ordinal) >= 0)
                return true;
        }
        return false;
    }

    private static string? NamespaceOf(FileDescriptorSet set) =>
        set.Files
            .Select(f => f.Options?.CsharpNamespace)
            .FirstOrDefault(ns => !string.IsNullOrEmpty(ns));

    private static CodeEmitter.Resolver BuildResolver(FileDescriptorSet set)
    {
        var map = new Dictionary<string, DescriptorProto>();

        foreach (var file in set.Files)
        {
            var prefix = string.IsNullOrEmpty(file.Package) ? "" : file.Package;

            foreach (var msg in file.MessageTypes)
            {
                Index(msg, prefix, map);
            }
        }

        return name => {
            var key = name.TrimStart('.');
            return map.TryGetValue(key, out var d) ? d : null;
        };
    }

    private static void Index(DescriptorProto d, string prefix, Dictionary<string, DescriptorProto> map)
    {
        var fq = string.IsNullOrEmpty(prefix) ? d.Name : $"{prefix}.{d.Name}";
        map[fq] = d;

        foreach (var nested in d.NestedTypes)
        {
            Index(nested, fq, map);
        }
    }

    /// <summary>
    /// Builds the proto-FQ-name -> dotted-C#-path resolver. The proto key carries the package and
    /// message nesting (matching <see cref="FieldDescriptorProto.TypeName"/>); the C# value drops
    /// the package and keeps only the message nesting, prefix-stripped per segment.
    /// </summary>
    private static CodeEmitter.CsName BuildCsNames(FileDescriptorSet set)
    {
        var map = new Dictionary<string, string>();

        foreach (var file in set.Files)
        {
            var prefix = string.IsNullOrEmpty(file.Package) ? "" : file.Package;

            foreach (var msg in file.MessageTypes)
            {
                IndexCs(msg, prefix, "", map);
            }

            foreach (var e in file.EnumTypes)
            {
                map[string.IsNullOrEmpty(prefix) ? e.Name : $"{prefix}.{e.Name}"] = CodeEmitter.StripPrefix(e.Name);
            }
        }

        return name => {
            var key = name.TrimStart('.');
            return map.TryGetValue(key, out var v) ? v : null;
        };
    }

    private static void IndexCs(DescriptorProto d, string protoPrefix, string csPrefix, Dictionary<string, string> map)
    {
        var fq = string.IsNullOrEmpty(protoPrefix) ? d.Name : $"{protoPrefix}.{d.Name}";
        var cs = string.IsNullOrEmpty(csPrefix) ? CodeEmitter.StripPrefix(d.Name) : $"{csPrefix}.{CodeEmitter.StripPrefix(d.Name)}";
        map[fq] = cs;
        // Nested types are emitted inside a `Types` container class (see EmitPoco), so their
        // dotted C# path gains a `.Types` segment per level of message nesting.
        var nestedPrefix = $"{cs}.Types";

        foreach (var nested in d.NestedTypes)
        {
            IndexCs(nested, fq, nestedPrefix, map);
        }

        foreach (var e in d.EnumTypes)
        {
            map[$"{fq}.{e.Name}"] = $"{nestedPrefix}.{CodeEmitter.StripPrefix(e.Name)}";
        }
    }

    private static Dictionary<string, int> ScanCmdIds(IEnumerable<Proto> files)
    {
        var map = new Dictionary<string, int>();

        foreach (var f in files)
        {
            // Preferred: a `// CmdId: <n>` comment immediately preceding a message.
            foreach (Match m in Regex.Matches(f.Content, @"//\s*CmdId\s*:\s*(-?\d+)\s*[\r\n]+\s*message\s+(\w+)"))
            {
                if (int.TryParse(m.Groups[1].Value, out var id))
                    map[m.Groups[2].Value] = id;
            }

            // Fallback: an `enum CmdId { CMD_ID = <n>; }` inside the message body.
            foreach (Match m in Regex.Matches(f.Content, @"message\s+(\w+)\s*\{(.*?)\}", RegexOptions.Singleline))
            {
                var name = m.Groups[1].Value;
                if (map.ContainsKey(name)) continue;

                var e = Regex.Match(m.Groups[2].Value, @"CMD_ID\s*=\s*(-?\d+)");

                if (e.Success && int.TryParse(e.Groups[1].Value, out var id))
                    map[name] = id;
            }
        }

        return map;
    }
}
