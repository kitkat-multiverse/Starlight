using System.Collections.Immutable;
using System.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Starlight.Protobuf.Compiler;

var protoFiles = Directory.GetFiles(
    Environment.CurrentDirectory,
    "*.proto",
    SearchOption.AllDirectories);

Console.WriteLine($"Found {protoFiles.Length} .proto file(s):");

foreach (var file in protoFiles)
{
    Console.WriteLine($"  {file}");
}
Console.WriteLine();

ImmutableArray<AdditionalText> additionalTexts =
    [.. protoFiles.Select(AdditionalText (f) => new ProtoText(f))];

var compilation = CSharpCompilation.Create(
    "Starlight.Protobuf.Compiler.DebugHost",
    syntaxTrees: null,
    [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
    new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

var generator = new ProtobufCompiler();

var driver = CSharpGeneratorDriver
    .Create(generator)
    .AddAdditionalTexts(additionalTexts);

var sw = Stopwatch.StartNew();
driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out var driverDiagnostics);
sw.Stop();

var result = driver.GetRunResult();

Console.WriteLine($"Generator ran in {sw.ElapsedMilliseconds} ms.");
Console.WriteLine();

var diagnostics = driverDiagnostics
    .Concat(result.Results.SelectMany(r => r.Diagnostics))
    .ToArray();

Console.WriteLine($"Diagnostics ({diagnostics.Length}):");

foreach (var diagnostic in diagnostics)
{
    Console.WriteLine($"  {diagnostic.Severity}: {diagnostic}");
}

if (diagnostics.Length == 0)
    Console.WriteLine("  (none)");
Console.WriteLine();

var generatedSources = result.Results.SelectMany(r => r.GeneratedSources).ToArray();
Console.WriteLine($"Generated sources ({generatedSources.Length}):");

foreach (var source in generatedSources)
{
    Console.WriteLine($"// ==================== {source.HintName} ====================");
    Console.WriteLine(source.SourceText);
}

if (generatedSources.Length == 0)
    Console.WriteLine("  (none)");

// Surface an exception thrown inside the generator (the driver swallows it into a diagnostic).
var crash = result.Results
    .Select(r => r.Exception)
    .FirstOrDefault(e => e is not null);

if (crash is not null)
{
    Console.WriteLine();
    Console.WriteLine("Generator threw:");
    Console.WriteLine(crash);
    return 1;
}

return 0;

file sealed class ProtoText(string path) : AdditionalText
{
    public override string Path { get; } = path;

    public override SourceText GetText(CancellationToken cancellationToken = default) =>
        SourceText.From(File.ReadAllText(Path), System.Text.Encoding.UTF8);
}
