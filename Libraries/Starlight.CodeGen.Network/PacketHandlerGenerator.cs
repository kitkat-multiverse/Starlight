using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Starlight.CodeGen.Network;

/// <summary>
/// Generates a static <c>PacketDispatcher.Dispatch</c> method that routes a deserialized
/// protocol message to the matching <c>[Opcode]</c>-annotated handler. The dispatch key is
/// the message's runtime type (a single <c>switch</c>); handler signatures are analyzed so
/// parameters (session / <c>GamePacket</c> / <c>PacketHead</c> / the message) and return
/// types (sync, <c>Task</c>/<c>ValueTask</c>, single/enumerable <c>IMessage</c>) are bound
/// automatically. A non-null returned message is sent back through <c>session.Send</c>.
/// </summary>
[Generator]
public sealed class PacketHandlerGenerator : IIncrementalGenerator
{
    private const string OpcodeAttributeName = "Starlight.Protocol.OpcodeAttribute";
    private const string MessageName = "Starlight.Protobuf.Core.IMessage";
    private const string GamePacketName = "Starlight.Protocol.GamePacket";
    private const string PacketHeadName = "Starlight.Protocol.PacketHead";
    private const string SessionProperty = "build_property.PacketHandlerSession";

    private static readonly SymbolDisplayFormat FullyQualified = SymbolDisplayFormat.FullyQualifiedFormat;

    private static readonly DiagnosticDescriptor UnusedParameters = new(
        "SLNET001", "Packet handler parameters are unused",
        "Packet handler '{0}' declares parameters but uses none of them; remove the unused parameters",
        "Starlight.PacketHandlers", DiagnosticSeverity.Warning, true);

    private static readonly DiagnosticDescriptor Unreachable = new(
        "SLNET002", "Packet handler is unreachable from the session",
        "Packet handler '{0}' lives in type '{1}', which has no property or field on session type '{2}'",
        "Starlight.PacketHandlers", DiagnosticSeverity.Warning, true);

    private static readonly DiagnosticDescriptor UnsupportedParameter = new(
        "SLNET003", "Unsupported packet handler parameter",
        "Packet handler '{0}' parameter '{1}' has unsupported type '{2}'; allowed: the session type, GamePacket, PacketHead, or a message type",
        "Starlight.PacketHandlers", DiagnosticSeverity.Error, true);

    private static readonly DiagnosticDescriptor UninferableMessage = new(
        "SLNET004", "Cannot infer packet handler message type",
        "Packet handler '{0}' has no single message-typed parameter to infer the opcode from; declare exactly one message parameter or pass the type explicitly as [Opcode(typeof(...))]",
        "Starlight.PacketHandlers", DiagnosticSeverity.Error, true);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var sessionName = context.AnalyzerConfigOptionsProvider.Select((provider, _) =>
            provider.GlobalOptions.TryGetValue(SessionProperty, out var name) ? name : null);

        var input = context.CompilationProvider.Combine(sessionName);
        context.RegisterSourceOutput(input, static (spc, pair) => Generate(spc, pair.Left, pair.Right));
    }

    private static void Generate(SourceProductionContext spc, Compilation compilation, string? sessionName)
    {
        if (string.IsNullOrWhiteSpace(sessionName))
            return;

        var sessionType = compilation.GetTypeByMetadataName(sessionName!);
        var opcodeAttr = compilation.GetTypeByMetadataName(OpcodeAttributeName);
        var iMessage = compilation.GetTypeByMetadataName(MessageName);
        var gamePacket = compilation.GetTypeByMetadataName(GamePacketName);
        var packetHead = compilation.GetTypeByMetadataName(PacketHeadName);

        // Without these we can't even form the dispatch signature.
        if (sessionType is null || opcodeAttr is null || iMessage is null || gamePacket is null)
            return;

        var task1 = compilation.GetTypeByMetadataName("System.Threading.Tasks.Task`1");
        var valueTask1 = compilation.GetTypeByMetadataName("System.Threading.Tasks.ValueTask`1");
        var task = compilation.GetTypeByMetadataName("System.Threading.Tasks.Task");
        var valueTask = compilation.GetTypeByMetadataName("System.Threading.Tasks.ValueTask");

        var methods = new List<IMethodSymbol>();
        CollectHandlers(compilation.Assembly.GlobalNamespace, opcodeAttr, methods);

        var models = new List<Handler>();

        foreach (var method in methods)
        {
            ReportUnusedParameters(spc, compilation, method);

            var attribute = method.GetAttributes().First(a =>
                SymbolEqualityComparer.Default.Equals(a.AttributeClass, opcodeAttr));

            var messageType = ResolveMessageType(attribute, method, iMessage);
            if (messageType is null)
            {
                spc.ReportDiagnostic(Diagnostic.Create(UninferableMessage,
                    method.Locations.FirstOrDefault(), method.Name));
                continue;
            }

            var access = FindAccessPath(sessionType, method.ContainingType);
            if (access is null)
            {
                spc.ReportDiagnostic(Diagnostic.Create(Unreachable,
                    method.Locations.FirstOrDefault(), method.Name, method.ContainingType.Name, sessionType.Name));
                continue;
            }

            var args = new List<string>();
            var valid = true;
            foreach (var parameter in method.Parameters)
            {
                var argument = BindParameter(parameter.Type, sessionType, gamePacket, packetHead, iMessage);
                if (argument is null)
                {
                    spc.ReportDiagnostic(Diagnostic.Create(UnsupportedParameter,
                        parameter.Locations.FirstOrDefault(), method.Name, parameter.Name,
                        parameter.Type.ToDisplayString()));
                    valid = false;
                    break;
                }

                args.Add(argument);
            }

            if (!valid)
                continue;

            var priority = 0;
            foreach (var named in attribute.NamedArguments)
                if (named.Key == "Priority" && named.Value.Value is int value)
                    priority = value;

            var ret = ClassifyReturn(method.ReturnType, iMessage, task1, valueTask1, task, valueTask, packetHead);
            models.Add(new Handler(
                messageType.ToDisplayString(FullyQualified), access, method.Name, args, ret, priority));
        }

        // One switch case per message type; handlers within a case run by descending
        // priority, ties broken alphabetically by method name for a stable build order.
        var groups = models
            .GroupBy(m => m.MessageType)
            .Select(g => g
                .OrderByDescending(m => m.Priority)
                .ThenBy(m => m.Method, StringComparer.Ordinal)
                .ToList())
            .ToList();

        var anyAsync = models.Any(m => m.Return.Awaitable);

        var ns = sessionType.ContainingNamespace.ToDisplayString();
        var sessionFq = sessionType.ToDisplayString(FullyQualified);
        var packetFq = gamePacket.ToDisplayString(FullyQualified);
        var messageFq = iMessage.ToDisplayString(FullyQualified);

        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine($"namespace {ns};");
        sb.AppendLine();
        sb.AppendLine("/// <summary>Generated packet dispatcher; switches on the message runtime type.</summary>");
        sb.AppendLine("public static class PacketDispatcher");
        sb.AppendLine("{");
        sb.AppendLine("    /// <summary>Routes <paramref name=\"message\"/> to its handler. Returns <c>true</c> when handled.</summary>");
        sb.AppendLine($"    public static {(anyAsync ? "async " : "")}global::System.Threading.Tasks.ValueTask<bool> Dispatch({sessionFq} session, {packetFq} packet, {messageFq} message)");
        sb.AppendLine("    {");
        sb.AppendLine("        switch (message)");
        sb.AppendLine("        {");

        foreach (var group in groups)
        {
            var usesMessage = group.Any(h => h.Arguments.Contains("msg"));
            sb.AppendLine($"            case {group[0].MessageType} {(usesMessage ? "msg" : "_")}:");
            sb.AppendLine("            {");

            for (var i = 0; i < group.Count; i++)
            {
                var handler = group[i];
                var invoke = $"{handler.Access}.{handler.Method}({string.Join(", ", handler.Arguments)})";
                var call = handler.Return.Awaitable ? $"await {invoke}" : invoke;

                switch (handler.Return.Send)
                {
                    case SendKind.None:
                        sb.AppendLine($"                {call};");
                        break;
                    case SendKind.Single:
                        sb.AppendLine($"                var __result{i} = {call};");
                        sb.AppendLine($"                if (__result{i} is not null) session.Send(__result{i});");
                        break;
                    case SendKind.SingleWithHead:
                        sb.AppendLine($"                var (__result{i}, __head{i}) = {call};");
                        sb.AppendLine($"                if (__result{i} is not null) session.Send(__result{i}, __head{i});");
                        break;
                    case SendKind.Many:
                        sb.AppendLine($"                var __results{i} = {call};");
                        sb.AppendLine($"                if (__results{i} is not null)");
                        sb.AppendLine("                {");
                        sb.AppendLine($"                    foreach (var __message in __results{i})");
                        sb.AppendLine("                        if (__message is not null) session.Send(__message);");
                        sb.AppendLine("                }");
                        break;
                }
            }

            sb.AppendLine($"                {Return(anyAsync, true)}");
            sb.AppendLine("            }");
        }

        sb.AppendLine("            default:");
        sb.AppendLine($"                {Return(anyAsync, false)}");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine("}");

        spc.AddSource("PacketDispatcher.g.cs", sb.ToString());
    }

    private static string Return(bool async, bool value)
    {
        var literal = value ? "true" : "false";
        return async
            ? $"return {literal};"
            : $"return new global::System.Threading.Tasks.ValueTask<bool>({literal});";
    }

    /// <summary>
    /// Determines the message type a handler is keyed on: the explicit <c>[Opcode(typeof(T))]</c>
    /// argument when present, otherwise the handler's single message-typed parameter. Returns
    /// <c>null</c> when neither yields an unambiguous message type.
    /// </summary>
    private static INamedTypeSymbol? ResolveMessageType(
        AttributeData attribute, IMethodSymbol method, INamedTypeSymbol iMessage)
    {
        if (attribute.ConstructorArguments.Length > 0 &&
            attribute.ConstructorArguments[0].Value is INamedTypeSymbol explicitType)
            return explicitType;

        INamedTypeSymbol? inferred = null;
        foreach (var parameter in method.Parameters)
        {
            if (parameter.Type is not INamedTypeSymbol named ||
                SymbolEqualityComparer.Default.Equals(named, iMessage) ||
                !IsAssignable(named, iMessage))
                continue;

            if (inferred is not null)
                return null; // ambiguous: more than one message-typed parameter
            inferred = named;
        }

        return inferred;
    }

    private static void CollectHandlers(INamespaceSymbol ns, INamedTypeSymbol attribute, List<IMethodSymbol> output)
    {
        foreach (var type in ns.GetTypeMembers())
            CollectFromType(type, attribute, output);
        foreach (var child in ns.GetNamespaceMembers())
            CollectHandlers(child, attribute, output);
    }

    private static void CollectFromType(INamedTypeSymbol type, INamedTypeSymbol attribute, List<IMethodSymbol> output)
    {
        foreach (var member in type.GetMembers())
            if (member is IMethodSymbol method &&
                method.GetAttributes().Any(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, attribute)))
                output.Add(method);

        foreach (var nested in type.GetTypeMembers())
            CollectFromType(nested, attribute, output);
    }

    /// <summary>Finds how to reach an instance of <paramref name="module"/> from <c>session</c>.</summary>
    private static string? FindAccessPath(INamedTypeSymbol session, INamedTypeSymbol module)
    {
        if (IsAssignable(session, module))
            return "session";

        foreach (var member in EnumerateMembers(session))
        {
            var memberType = member switch
            {
                IPropertySymbol { GetMethod: not null, IsWriteOnly: false } p => p.Type,
                IFieldSymbol f => f.Type,
                _ => null
            };

            if (memberType is not null && IsAssignable(memberType, module))
                return $"session.{member.Name}";
        }

        return null;
    }

    private static IEnumerable<ISymbol> EnumerateMembers(INamedTypeSymbol type)
    {
        var seen = new HashSet<ISymbol>(SymbolEqualityComparer.Default);

        for (var current = type; current is not null; current = current.BaseType)
        {
            foreach (var member in current.GetMembers().Where(member => seen.Add(member)))
                yield return member;
        }

        foreach (var iface in type.AllInterfaces)
        {
            foreach (var member in iface.GetMembers().Where(member => seen.Add(member)))
                yield return member;
        }
    }

    private static string? BindParameter(
        ITypeSymbol parameter, INamedTypeSymbol session, INamedTypeSymbol gamePacket,
        INamedTypeSymbol? packetHead, INamedTypeSymbol iMessage)
    {
        if (SymbolEqualityComparer.Default.Equals(parameter, gamePacket))
            return "packet";
        if (packetHead is not null && SymbolEqualityComparer.Default.Equals(parameter, packetHead))
            return "packet.Metadata.Value";
        if (IsAssignable(session, parameter))
            return "session";
        if (IsAssignable(parameter, iMessage))
            return "msg";
        return null;
    }

    private static ReturnInfo ClassifyReturn(
        ITypeSymbol returnType, INamedTypeSymbol iMessage,
        INamedTypeSymbol? task1, INamedTypeSymbol? valueTask1, INamedTypeSymbol? task, INamedTypeSymbol? valueTask,
        INamedTypeSymbol? packetHead)
    {
        var awaitable = false;
        var inner = returnType;

        if (returnType is INamedTypeSymbol named)
        {
            var definition = named.ConstructedFrom;
            if (SymbolEqualityComparer.Default.Equals(definition, task1) ||
                SymbolEqualityComparer.Default.Equals(definition, valueTask1))
            {
                awaitable = true;
                inner = named.TypeArguments[0];
            }
            else if (SymbolEqualityComparer.Default.Equals(named, task) ||
                     SymbolEqualityComparer.Default.Equals(named, valueTask))
            {
                return new ReturnInfo(true, SendKind.None);
            }
        }

        if (inner.SpecialType == SpecialType.System_Void)
            return new ReturnInfo(awaitable, SendKind.None);

        if (packetHead is not null &&
            inner is INamedTypeSymbol { IsTupleType: true } tuple &&
            tuple.TupleElements.Length == 2 &&
            IsAssignable(tuple.TupleElements[0].Type, iMessage) &&
            SymbolEqualityComparer.Default.Equals(tuple.TupleElements[1].Type, packetHead))
            return new ReturnInfo(awaitable, SendKind.SingleWithHead);

        var item = GetEnumerableItem(inner);
        if (item is not null && IsAssignable(item, iMessage))
            return new ReturnInfo(awaitable, SendKind.Many);
        if (IsAssignable(inner, iMessage))
            return new ReturnInfo(awaitable, SendKind.Single);

        return new ReturnInfo(awaitable, SendKind.None);
    }

    private static ITypeSymbol? GetEnumerableItem(ITypeSymbol type)
    {
        if (type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Collections_Generic_IEnumerable_T } named)
            return named.TypeArguments[0];

        foreach (var iface in type.AllInterfaces)
        {
            if (iface.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T)
                return iface.TypeArguments[0];
        }

        return null;
    }

    /// <summary>True if a <paramref name="source"/> value can be used where <paramref name="destination"/> is expected.</summary>
    private static bool IsAssignable(ITypeSymbol source, ITypeSymbol destination)
    {
        if (SymbolEqualityComparer.Default.Equals(source, destination))
            return true;

        for (var baseType = source.BaseType; baseType is not null; baseType = baseType.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(baseType, destination))
                return true;
        }

        foreach (var iface in source.AllInterfaces)
        {
            if (SymbolEqualityComparer.Default.Equals(iface, destination))
                return true;
        }

        return false;
    }

    private static void ReportUnusedParameters(SourceProductionContext spc, Compilation compilation, IMethodSymbol method)
    {
        if (method.Parameters.Length == 0)
            return;

        if (method.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() is not MethodDeclarationSyntax declaration)
            return;

        var body = (SyntaxNode?)declaration.Body ?? declaration.ExpressionBody?.Expression;
        if (body is null)
            return;

        var model = compilation.GetSemanticModel(declaration.SyntaxTree);
        var flow = model.AnalyzeDataFlow(body);
        if (!flow.Succeeded)
            return;

        var used = new HashSet<ISymbol>(
            flow.ReadInside.Concat(flow.WrittenInside).Concat(flow.Captured),
            SymbolEqualityComparer.Default);

        if (!method.Parameters.Any(used.Contains))
            spc.ReportDiagnostic(Diagnostic.Create(UnusedParameters, declaration.Identifier.GetLocation(), method.Name));
    }
}

internal enum SendKind { None, Single, SingleWithHead, Many }

internal readonly struct ReturnInfo(bool awaitable, SendKind send)
{
    public bool Awaitable { get; } = awaitable;
    public SendKind Send { get; } = send;
}

internal readonly struct Handler(
    string messageType, string access, string method, List<string> arguments, ReturnInfo returnInfo, int priority)
{
    public string MessageType { get; } = messageType;
    public string Access { get; } = access;
    public string Method { get; } = method;
    public List<string> Arguments { get; } = arguments;
    public ReturnInfo Return { get; } = returnInfo;
    public int Priority { get; } = priority;
}
