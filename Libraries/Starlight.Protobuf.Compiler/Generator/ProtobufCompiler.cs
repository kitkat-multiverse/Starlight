using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;
using Google.Protobuf.Reflection;
using Microsoft.CodeAnalysis;

namespace Starlight.Protobuf.Compiler;

[Generator]
public sealed partial class ProtobufCompiler : IIncrementalGenerator
{
    private static readonly DiagnosticDescriptor ParseError = new(
        "SLPB001",
        "Protobuf parse error",
        "{0}({1},{2}): {3}",
        "Starlight.Protobuf",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor MissingMetaError = new(
        "SLPB002",
        "Missing protobuf metadata",
        "A {0} proto group must include *meta.proto with a package declaration",
        "Starlight.Protobuf",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor ReservedNameError = new(
        "SLPB003",
        "Reserved C# name",
        "Proto {0} '{1}' generates the C# name '{2}', which {3}. Rename the proto {0}.",
        "Starlight.Protobuf",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor FieldTypeMismatchError = new(
        "SLPB004",
        "Protobuf field type mismatch",
        "Field '{0}' on message '{1}' is {2} in the base but {3} in version '{4}'. The serializer derives its wire codec from the base type, so this field would (de)serialize with the wrong wire format. Reconcile the types.",
        "Starlight.Protobuf",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor MaskGrammarError = new(
        "SLPB005",
        "Unsupported protobuf mask expression",
        "Field '{0}' on message '{1}' has mask \"{2}\" containing tokens outside the allowed grammar (value, integer literals, parentheses, and + - ^). Only invertible arithmetic masks are supported. Fix the mask.",
        "Starlight.Protobuf",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor NonInvertibleMaskError = new(
        "SLPB006",
        "Non-invertible protobuf mask",
        "Field '{0}' on message '{1}' has a non-invertible mask \"{2}\". The compiler inverts masks at build time to generate the decode path, so the mask must be a left-deep, fully-parenthesized arithmetic chain (e.g. (value - 1) ^ 2). Fix the mask.",
        "Starlight.Protobuf",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor TransformUnsupportedError = new(
        "SLPB007",
        "Unsupported protobuf field transform",
        "Field '{0}' on message '{1}' declares a value transform but is {2}. Transforms apply only to singular integer fields (int32/64, uint32/64, sint32/64, fixed32/64, sfixed32/64). Remove the transform option.",
        "Starlight.Protobuf",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private const string Roof = "Starlight.Protobuf";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var protos = context.AdditionalTextsProvider
            .Where(f => f.Path.EndsWith(".proto"))
            .Combine(context.AnalyzerConfigOptionsProvider)
            .Select((pair, ct) => {
                var (file, provider) = pair;
                provider.GetOptions(file).TryGetValue("build_metadata.AdditionalFiles.SLProtoRole", out var role);

                return new Proto(
                    Path.GetFileName(file.Path),
                    file.Path,
                    file.GetText(ct)?.ToString() ?? "",
                    role ?? "");
            })
            .Collect();

        context.RegisterSourceOutput(protos, Generate);
    }

    private readonly struct Proto
    {
        public Proto(string fileName, string fullPath, string content, string role)
        {
            FileName = fileName;
            FullPath = fullPath;
            Content = content;
            Role = role;
        }

        public string FileName { get; }
        public string FullPath { get; }
        public string Content { get; }
        public string Role { get; }

        public string ResolvedRole
        {
            get
            {
                if (!string.IsNullOrEmpty(Role)) return Role;

                var p = FullPath.Replace(oldChar: '\\', newChar: '/');
                if (p.Contains("/Base/")) return "Base";
                if (FileName == "extra.proto") return "Independent";

                return "Version";
            }
        }
    }

    private static void ReportMaskViolations(SourceProductionContext ctx, CodeEmitter.TransformTable transforms)
    {
        foreach (var v in transforms.Violations)
        {
            ctx.ReportDiagnostic(Diagnostic.Create(
                v.Invalid ? MaskGrammarError : NonInvertibleMaskError,
                Location.None, v.Field, v.Message, v.Mask));
        }
    }

    private static void Generate(SourceProductionContext ctx, ImmutableArray<Proto> protos)
    {
        if (protos.IsDefaultOrEmpty) return;

        var baseFiles = protos.Where(p => p.ResolvedRole == "Base").ToList();
        var referenceFiles = protos.Where(p => p.ResolvedRole == "Reference").ToList();
        var versionFiles = protos.Where(p => p.ResolvedRole == "Version").ToList();
        var independentFiles = protos.Where(p => p.ResolvedRole == "Independent").ToList();
        // Role "Import" is intentionally unhandled: such protos exist in `protos`
        // only so other files' `import` directives resolve (e.g. a version project
        // links extra.proto for its custom option extensions). Never parsed as a
        // base, never correlated, never emitted.

        // --- Base: parse for correlation; emit POCOs only when this compilation
        //     owns the base. A version project links the base (and extra) protos
        //     as "Reference" so its messages correlate by name and its imports
        //     resolve, without re-emitting the canonical POCOs into every
        //     per-version assembly. The POCOs live once, in the Base project. ---
        var baseInput = baseFiles.Concat(referenceFiles).ToList();
        var ownsBase = baseFiles.Count > 0;
        var baseSet = baseInput.Count > 0 ? Parse(ctx, baseInput, protos) : null;
        var baseNs = "Generated";
        var baseByName = new Dictionary<string, DescriptorProto>();
        CodeEmitter.Resolver baseResolver = _ => null;
        CodeEmitter.CsName baseCsNames = _ => null;
        var cmdIds = ScanCmdIds(baseInput.Concat(versionFiles));

        if (baseSet is not null)
        {
            baseNs = NamespaceOf(baseSet) ?? baseNs;
            baseResolver = BuildResolver(baseSet);
            baseCsNames = BuildCsNames(baseSet);

            // Keyed by prefix-stripped name: version messages carry a leading '_'
            // custom-name marker that the emitter strips, so correlation must too
            // (e.g. version '_AkaFesDetailInfo' correlates to base 'AkaFesDetailInfo').
            // First base wins on a strip collision; the base schema is curated.
            foreach (var msg in baseSet.Files.SelectMany(f => f.MessageTypes))
            {
                var key = CodeEmitter.StripPrefix(msg.Name);
                if (!baseByName.ContainsKey(key)) baseByName[key] = msg;
            }

            if (ownsBase)
            {
                // Emit/validate only the files this project owns. Imports pulled in to
                // resolve `import` directives (e.g. define.proto, extra.proto) appear in
                // baseSet.Files too, but their POCOs are emitted by their owning project.
                var baseOwned = new HashSet<string>(baseFiles.Select(f => f.FileName));
                var ownedFiles = baseSet.Files.Where(f => baseOwned.Contains(f.Name)).ToList();

                ValidateNames(ctx, ownedFiles.SelectMany(f => f.MessageTypes),
                    ownedFiles.SelectMany(f => f.EnumTypes), cmdIds);

                foreach (var file in ownedFiles)
                {
                    if (file.MessageTypes.Count == 0 && file.EnumTypes.Count == 0) continue;

                    var body = new StringBuilder();

                    foreach (var e in file.EnumTypes)
                    {
                        EmitTopLevelEnum(body, e);
                    }

                    foreach (var msg in file.MessageTypes)
                    {
                        CodeEmitter.EmitPoco(body, msg, baseNs, cmdIds.TryGetValue(msg.Name, out var id) ? id : (int?)null, baseResolver,
                            baseCsNames);
                        body.AppendLine();
                    }

                    ctx.AddSource($"{Stem(file.Name)}.Poco.g.cs", Wrap(baseNs, body.ToString()));
                }
            }
        }

        // --- Version: parse + emit serializers + registry -------------------
        if (versionFiles.Count > 0)
        {
            var versionSet = Parse(ctx, versionFiles, protos);
            var meta = versionSet.Files.FirstOrDefault(f => f.Name.EndsWith("meta.proto"));

            if (meta is null || string.IsNullOrEmpty(meta.Package))
            {
                var projectDir = Path.GetDirectoryName(versionFiles[0].FullPath) ?? versionFiles[0].FullPath;
                ctx.ReportDiagnostic(Diagnostic.Create(MissingMetaError, Location.None, projectDir));
            } else
            {
                var version = Capitalize(meta.Package);
                var versionNs = $"{baseNs}.{version}";

                var transforms = CodeEmitter.ReadTransforms(versionSet);
                ReportMaskViolations(ctx, transforms);

                // alts (alternate field names) are declared on the base proto.
                var alts = baseSet is not null ? CodeEmitter.ReadAlts(baseSet) : null;

                // Correlate only the version project's own files; imported base/extra/define
                // protos also appear in versionSet.Files but are emitted by their owners.
                var versionOwned = new HashSet<string>(versionFiles.Select(f => f.FileName));

                var correlated = versionSet.Files
                    .Where(f => versionOwned.Contains(f.Name))
                    .SelectMany(f => f.MessageTypes)
                    .Where(vm => baseByName.ContainsKey(CodeEmitter.StripPrefix(vm.Name)))
                    .Select(vm => (Version: vm, Base: baseByName[CodeEmitter.StripPrefix(vm.Name)]))
                    // Two version messages can strip to the same base name -- an obfuscated
                    // message colliding with a canonical one (e.g. a junk 'CustomGalleryInfo'
                    // alongside the real '_CustomGalleryInfo'). Emitting both yields duplicate
                    // serializer classes; keep the one whose fields best overlap the base.
                    .GroupBy(p => p.Base.Name)
                    .Select(g => g.OrderByDescending(p => StructuralOverlap(p.Base, p.Version)).First())
                    .ToList();

                var serializers = new StringBuilder();

                foreach (var (vm, bm) in correlated)
                {
                    ValidateFieldTypes(ctx, bm, vm, version, alts);
                    ValidateTransforms(ctx, bm, vm, transforms, baseResolver, alts);

                    EmitSerializerTree(serializers, bm, vm, baseNs, baseResolver, baseCsNames, transforms, alts,
                        CodeEmitter.StripPrefix(bm.Name));
                    serializers.AppendLine();
                }

                ctx.AddSource($"{version}.Serializers.g.cs", Wrap(versionNs, serializers.ToString()));

                ctx.AddSource($"{version}.Registry.g.cs",
                    Wrap(versionNs, EmitRegistry(version, baseNs, correlated.Select(c => c.Base).ToList(), cmdIds)));
            }
        }

        // --- Independent: POCO + single serializer, no version system -------
        foreach (var file in independentFiles)
        {
            var set = Parse(ctx, [file], protos);
            var ns = NamespaceOf(set) ?? "Generated";
            var resolver = BuildResolver(set);
            var csNames = BuildCsNames(set);
            var transforms = CodeEmitter.ReadTransforms(set);
            ReportMaskViolations(ctx, transforms);

            var own = set.Files.Where(f => f.Name == file.FileName).ToList();
            ValidateNames(ctx, own.SelectMany(f => f.MessageTypes), own.SelectMany(f => f.EnumTypes), cmdIds, selfSerializable: true);

            var body = new StringBuilder();

            // Emit only the file itself; imported dependencies (e.g. descriptor.proto)
            // resolve through the filesystem but must not be POCO'd here.
            foreach (var f in set.Files.Where(f => f.Name == file.FileName))
            {
                foreach (var e in f.EnumTypes)
                {
                    EmitTopLevelEnum(body, e);
                }

                foreach (var msg in f.MessageTypes)
                {
                    CodeEmitter.EmitPoco(body, msg, ns, cmdIds.TryGetValue(msg.Name, out var id) ? id : (int?)null, resolver, csNames,
                        selfSerializable: true);
                    body.AppendLine();
                    ValidateTransforms(ctx, msg, msg, transforms, resolver, alts: null);
                    EmitSerializerTree(body, msg, msg, ns, resolver, csNames, transforms, alts: null, CodeEmitter.StripPrefix(msg.Name));
                    body.AppendLine();
                }
            }

            ctx.AddSource($"{Stem(file.FileName)}.Independent.g.cs", Wrap(ns, body.ToString()));
        }
    }

    // Count of base fields whose prefix-stripped name also appears (stripped) on the version
    // message. Tie-breaks which version message correlates to a base when several strip to the
    // same canonical name: the real target shares the base's field names, the colliding
    // obfuscated impostor does not.
    private static int StructuralOverlap(DescriptorProto baseMsg, DescriptorProto versionMsg)
    {
        var versionNames = new HashSet<string>();

        foreach (var f in versionMsg.Fields)
        {
            versionNames.Add(CodeEmitter.StripPrefix(f.Name));
        }
        var score = 0;

        foreach (var f in baseMsg.Fields)
        {
            if (versionNames.Contains(CodeEmitter.StripPrefix(f.Name))) score++;
        }
        return score;
    }

    // Emits a serializer for the message and, recursively, for each of its nested
    // messages. Nested serializers are flattened to top-level classes (their names
    // derive from the dotted csPath via SerBase), so this walks base/version nested
    // pairs by name — mirroring the field-name correlation used at the top level.
    private static void EmitSerializerTree(
        StringBuilder sb,
        DescriptorProto baseMsg,
        DescriptorProto versionMsg,
        string baseNs,
        CodeEmitter.Resolver resolve,
        CodeEmitter.CsName csNames,
        CodeEmitter.TransformTable? transforms,
        CodeEmitter.AltsTable? alts,
        string csPath
    )
    {
        CodeEmitter.EmitSerializer(sb, baseMsg, versionMsg, baseNs, resolve, csNames, transforms, alts, csPath);
        sb.AppendLine();

        // Nested pairs correlate by prefix-stripped name, mirroring the top-level rule.
        var versionNested = new Dictionary<string, DescriptorProto>();

        foreach (var n in versionMsg.NestedTypes)
        {
            if (n.Options?.MapEntry == true) continue;

            var key = CodeEmitter.StripPrefix(n.Name);
            if (!versionNested.ContainsKey(key)) versionNested[key] = n;
        }

        foreach (var nb in baseMsg.NestedTypes)
        {
            if (nb.Options?.MapEntry == true) continue;
            if (!versionNested.TryGetValue(CodeEmitter.StripPrefix(nb.Name), out var nv)) continue;

            EmitSerializerTree(sb, nb, nv, baseNs, resolve, csNames, transforms, alts, $"{csPath}.Types.{CodeEmitter.StripPrefix(nb.Name)}");
        }
    }
}
