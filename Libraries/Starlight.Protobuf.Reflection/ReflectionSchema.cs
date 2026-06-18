extern alias protonet;
using System.Text;
using System.Text.RegularExpressions;
using Starlight.Protobuf.Core;
using ProtoSet = protonet::Google.Protobuf.Reflection.FileDescriptorSet;
using ProtoMsg = protonet::Google.Protobuf.Reflection.DescriptorProto;
using ProtoField = protonet::Google.Protobuf.Reflection.FieldDescriptorProto;
using FType = protonet::Google.Protobuf.Reflection.FieldDescriptorProto.Type;
using Label = protonet::Google.Protobuf.Reflection.FieldDescriptorProto.Label;
using IProtoFileSystem = protonet::Google.Protobuf.Reflection.IFileSystem;

namespace Starlight.Protobuf.Reflection;

/// <summary>
/// The result of loading one or more <c>.proto</c> files at runtime: the field
/// tables (<see cref="MessageDescriptor"/>) for every message, plus the
/// <c>// CmdId:</c> map and known-first set used for version detection. Built by
/// <see cref="ReflectionSchema.Load(System.Collections.Generic.IReadOnlyDictionary{string,string},string?)"/>
/// and consumed by <see cref="ReflectionRegistry"/>.
/// </summary>
public sealed class ReflectionSchema
{
    private static readonly HashSet<string> KnownFirstNames = [
        "GetPlayerTokenReq", "PingReq"
    ];

    private ReflectionSchema(
        string version,
        IReadOnlyDictionary<string, MessageDescriptor> byName,
        IReadOnlyDictionary<int, string> cmdIdToName,
        IReadOnlyDictionary<string, int> nameToCmdId,
        IReadOnlySet<int> knownFirst
    )
    {
        Version = version;
        ByName = byName;
        CmdIdToName = cmdIdToName;
        NameToCmdId = nameToCmdId;
        KnownFirst = knownFirst;
    }

    public string Version { get; }
    public IReadOnlyDictionary<string, MessageDescriptor> ByName { get; }
    public IReadOnlyDictionary<int, string> CmdIdToName { get; }
    public IReadOnlyDictionary<string, int> NameToCmdId { get; }
    public IReadOnlySet<int> KnownFirst { get; }

    /// <summary>Loads every <c>.proto</c> file in a directory (non-recursive).</summary>
    public static ReflectionSchema LoadFromDirectory(string directory, string? version = null)
    {
        var sources = Directory.EnumerateFiles(directory, "*.proto")
            .ToDictionary(Path.GetFileName, File.ReadAllText);
        return Load(sources, version);
    }

    /// <summary>Parses <c>.proto</c> sources (keyed by file name) into descriptors.</summary>
    public static ReflectionSchema Load(IReadOnlyDictionary<string, string> sources, string? version = null)
    {
        var set = new ProtoSet { FileSystem = new InMemoryFileSystem(sources) };

        foreach (var kv in sources)
        {
            set.Add(kv.Key, includeInOutput: true, new StringReader(kv.Value));
        }
        set.Process();

        var errors = set.GetErrors().Where(e => e.IsError).ToList();

        if (errors.Count > 0)
            throw new InvalidOperationException(
                "Failed to parse .proto sources:\n" + string.Join("\n", errors.Select(e => e.ToString())));

        var resolver = BuildResolver(set);
        var byName = new Dictionary<string, MessageDescriptor>();

        // Top-level messages (map entries are nested and handled inline).
        foreach (var msg in set.Files.SelectMany(f => f.MessageTypes))
        {
            BuildDescriptor(msg, byName, resolver);
        }

        var nameToCmdId = ScanCmdIds(sources.Values);
        var cmdIdToName = new Dictionary<int, string>();

        foreach (var kv in nameToCmdId)
        {
            cmdIdToName[kv.Value] = kv.Key;
        }

        var knownFirst = nameToCmdId
            .Where(kv => KnownFirstNames.Contains(kv.Key))
            .Select(kv => kv.Value)
            .ToHashSet();

        string resolvedVersion;

        if (version is not null)
            resolvedVersion = version;
        else if (set.Files.Select(f => f.Package).FirstOrDefault(p => !string.IsNullOrEmpty(p)) is {} pkg)
            resolvedVersion = Capitalize(pkg);
        else
            resolvedVersion = "Reflection";

        return new ReflectionSchema(resolvedVersion, byName, cmdIdToName, nameToCmdId, knownFirst);
    }

    // -- descriptor construction ---------------------------------------------

    private static MessageDescriptor BuildDescriptor(
        ProtoMsg msg,
        Dictionary<string, MessageDescriptor> byName,
        Func<string, ProtoMsg?> resolver
    )
    {
        if (byName.TryGetValue(msg.Name, out var existing)) return existing;

        var fields = new List<FieldDescriptor>();

        foreach (var field in msg.Fields)
        {
            fields.Add(BuildField(field, msg, byName, resolver));
        }

        MessageDescriptor descriptor = null!;

        descriptor = new MessageDescriptor(
            msg.Name, clrType: null, fields,
            () => new DynamicMessage(descriptor));
        byName[msg.Name] = descriptor;
        return descriptor;
    }

    private static FieldDescriptor BuildField(
        ProtoField field,
        ProtoMsg msg,
        Dictionary<string, MessageDescriptor> byName,
        Func<string, ProtoMsg?> resolver
    )
    {
        var prop = Prop(field.Name, msg.Name);

        // Lazy nested-message reference (resolved on first (de)serialization, by
        // which point every descriptor in this schema has been built).
        Func<MessageDescriptor>? MessageRef(string typeName)
        {
            var simple = Simple(typeName);

            return () => byName.TryGetValue(simple, out var d) ?
                d :
                throw new InvalidOperationException($"Unknown message type '{simple}' referenced by {msg.Name}.{field.Name}.");
        }

        if (IsMap(field, resolver, out var entry))
        {
            var keyField = entry!.Fields.First(f => f.Number == 1);
            var valField = entry.Fields.First(f => f.Number == 2);

            return new FieldDescriptor(
                field.Name, prop, field.Number, field.Number,
                Kind(valField.type), FieldRule.Map,
                keyKind: Kind(keyField.type),
                messageRef: valField.type == FType.TypeMessage ? MessageRef(valField.TypeName) : null);
        }

        if (field.label == Label.LabelRepeated)
            return new FieldDescriptor(
                field.Name, prop, field.Number, field.Number,
                Kind(field.type), FieldRule.Repeated,
                messageRef: field.type == FType.TypeMessage ? MessageRef(field.TypeName) : null);

        string? oneofName = null;
        var rule = FieldRule.Single;

        if (InRealOneof(field)) oneofName = Pascal(msg.OneofDecls[field.OneofIndex].Name);
        else if (field.Proto3Optional) rule = FieldRule.Optional;

        return new FieldDescriptor(
            field.Name, prop, field.Number, field.Number,
            Kind(field.type), rule,
            oneofName,
            messageRef: field.type == FType.TypeMessage ? MessageRef(field.TypeName) : null);
    }

    private static bool IsMap(ProtoField field, Func<string, ProtoMsg?> resolver, out ProtoMsg? entry)
    {
        entry = null;
        if (field.label != Label.LabelRepeated || field.type != FType.TypeMessage) return false;

        var d = resolver(field.TypeName);
        if (d?.Options?.MapEntry != true) return false;

        entry = d;
        return true;
    }

    private static bool InRealOneof(ProtoField f) => f.ShouldSerializeOneofIndex() && !f.Proto3Optional;

    private static ProtoKind Kind(FType type) => type switch {
        FType.TypeDouble => ProtoKind.Double,
        FType.TypeFloat => ProtoKind.Float,
        FType.TypeInt64 => ProtoKind.Int64,
        FType.TypeUint64 => ProtoKind.UInt64,
        FType.TypeInt32 => ProtoKind.Int32,
        FType.TypeFixed64 => ProtoKind.Fixed64,
        FType.TypeFixed32 => ProtoKind.Fixed32,
        FType.TypeBool => ProtoKind.Bool,
        FType.TypeString => ProtoKind.String,
        FType.TypeBytes => ProtoKind.Bytes,
        FType.TypeUint32 => ProtoKind.UInt32,
        FType.TypeSfixed32 => ProtoKind.SFixed32,
        FType.TypeSfixed64 => ProtoKind.SFixed64,
        FType.TypeSint32 => ProtoKind.SInt32,
        FType.TypeSint64 => ProtoKind.SInt64,
        FType.TypeEnum => ProtoKind.Enum,
        FType.TypeMessage => ProtoKind.Message,
        _ => throw new InvalidOperationException($"Unsupported proto type: {type}")
    };

    // -- parsing helpers ------------------------------------------------------

    private static Func<string, ProtoMsg?> BuildResolver(ProtoSet set)
    {
        var map = new Dictionary<string, ProtoMsg>();

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

    private static void Index(ProtoMsg d, string prefix, Dictionary<string, ProtoMsg> map)
    {
        var fq = string.IsNullOrEmpty(prefix) ? d.Name : $"{prefix}.{d.Name}";
        map[fq] = d;

        foreach (var nested in d.NestedTypes)
        {
            Index(nested, fq, map);
        }
    }

    private static Dictionary<string, int> ScanCmdIds(IEnumerable<string> sources)
    {
        var map = new Dictionary<string, int>();

        foreach (var content in sources)
        {
            foreach (Match m in Regex.Matches(content, @"//\s*CmdId\s*:\s*(-?\d+)\s*[\r\n]+\s*message\s+(\w+)"))
            {
                if (int.TryParse(m.Groups[1].Value, out var id))
                    map[m.Groups[2].Value] = id;
            }

            foreach (Match m in Regex.Matches(content, @"message\s+(\w+)\s*\{(.*?)\}", RegexOptions.Singleline))
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

    private static string Prop(string fieldName, string messageName)
    {
        var p = Pascal(fieldName);
        return p == messageName ? p + "_" : p;
    }

    private static string Pascal(string snake)
    {
        var sb = new StringBuilder(snake.Length);
        var upper = true;

        foreach (var c in snake)
        {
            if (c == '_')
            {
                upper = true;
                continue;
            }
            sb.Append(upper ? char.ToUpperInvariant(c) : c);
            upper = false;
        }

        return sb.ToString();
    }

    private static string Simple(string typeName)
    {
        var name = typeName.TrimStart('.');
        var dot = name.LastIndexOf('.');
        return dot < 0 ? name : name.Substring(dot + 1);
    }

    private static string Capitalize(string package) =>
        package.Length == 0 ? package : char.ToUpperInvariant(package[0]) + package.Substring(1);
}

internal sealed class InMemoryFileSystem(IReadOnlyDictionary<string, string> sources) : IProtoFileSystem
{
    public bool Exists(string path) => sources.ContainsKey(Normalize(path));

    public TextReader? OpenText(string path) =>
        sources.TryGetValue(Normalize(path), out var content) ? new StringReader(content) : null;

    private static string Normalize(string path) => Path.GetFileName(path);
}
