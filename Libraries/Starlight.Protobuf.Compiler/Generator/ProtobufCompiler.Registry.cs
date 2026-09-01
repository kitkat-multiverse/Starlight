using Google.Protobuf.Reflection;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Starlight.Protobuf.Compiler;

public sealed partial class ProtobufCompiler
{
    private static string EmitRegistry(string version, string baseNs, List<DescriptorProto> messages, Dictionary<string, int> cmdIds)
    {
        int? Cmd(DescriptorProto m)
        {
            return cmdIds.TryGetValue(m.Name, out var id) ? id : (int?)null;
        }

        var knownFirstNames = new HashSet<string> { "GetPlayerTokenReq", "PingReq" };

        var knownFirst = messages
            .Where(m => knownFirstNames.Contains(m.Name) && Cmd(m).HasValue)
            .Select(m => Cmd(m)!.Value)
            .Distinct()
            .ToList();

        var sb = new StringBuilder();
        sb.AppendLine($"public sealed class {version}ProtocolRegistry : global::Starlight.Protobuf.Registry.ProtocolRegistry");
        sb.AppendLine("{");
        sb.AppendLine($"    public override string Version => \"{version}\";");
        sb.AppendLine();
        sb.AppendLine("    public override global::System.Collections.Generic.IReadOnlySet<int> KnownFirst { get; } =");
        sb.AppendLine($"        new global::System.Collections.Generic.HashSet<int> {{ {string.Join(", ", knownFirst)} }};");
        sb.AppendLine();

        sb.AppendLine("    public override int GetCmdId(global::Starlight.Protobuf.Core.IMessage message) => message switch");
        sb.AppendLine("    {");

        foreach (var m in messages.Where(m => Cmd(m).HasValue))
        {
            sb.AppendLine($"        global::{baseNs}.{CodeEmitter.StripPrefix(m.Name)} => {Cmd(m)!.Value},");
        }
        sb.AppendLine("        _ => 0,");
        sb.AppendLine("    };");
        sb.AppendLine();

        sb.AppendLine("    public override global::Starlight.Protobuf.Core.IMessage Create(int cmdId) => cmdId switch");
        sb.AppendLine("    {");

        foreach (var m in messages.Where(m => Cmd(m).HasValue))
        {
            sb.AppendLine($"        {Cmd(m)!.Value} => new global::{baseNs}.{CodeEmitter.StripPrefix(m.Name)}(),");
        }

        sb.AppendLine(
            $"        _ => throw new global::System.ArgumentOutOfRangeException(nameof(cmdId), cmdId, \"Unknown CmdId for {version}.\"),");
        sb.AppendLine("    };");
        sb.AppendLine();

        sb.AppendLine("    public override int CalculateSize(global::Starlight.Protobuf.Core.IMessage message) => message switch");
        sb.AppendLine("    {");

        foreach (var m in messages)
        {
            sb.AppendLine(
                $"        global::{baseNs}.{CodeEmitter.StripPrefix(m.Name)} v => {CodeEmitter.StripPrefix(m.Name)}Serializer.Instance.CalculateSize(v),");
        }
        sb.AppendLine("        _ => 0,");
        sb.AppendLine("    };");
        sb.AppendLine();

        sb.AppendLine(
            "    public override void Serialize(global::Starlight.Protobuf.Core.IMessage message, global::Google.Protobuf.CodedOutputStream output)");
        sb.AppendLine("    {");
        sb.AppendLine("        switch (message)");
        sb.AppendLine("        {");

        foreach (var m in messages)
        {
            sb.AppendLine(
                $"            case global::{baseNs}.{CodeEmitter.StripPrefix(m.Name)} v: {CodeEmitter.StripPrefix(m.Name)}Serializer.Instance.Serialize(v, output); break;");
        }
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine();

        sb.AppendLine(
            "    public override void Deserialize(global::Starlight.Protobuf.Core.IMessage message, global::Google.Protobuf.CodedInputStream input)");
        sb.AppendLine("    {");
        sb.AppendLine("        switch (message)");
        sb.AppendLine("        {");

        foreach (var m in messages)
        {
            sb.AppendLine(
                $"            case global::{baseNs}.{CodeEmitter.StripPrefix(m.Name)} v: {CodeEmitter.StripPrefix(m.Name)}Serializer.Instance.Deserialize(v, input); break;");
        }
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine();

        sb.AppendLine(
            "    public override global::System.Collections.Generic.IReadOnlyCollection<global::Starlight.Protobuf.Core.MessageDescriptor> Descriptors { get; } =");
        sb.AppendLine("        new global::Starlight.Protobuf.Core.MessageDescriptor[]");
        sb.AppendLine("        {");

        foreach (var m in messages)
        {
            sb.AppendLine($"            {CodeEmitter.StripPrefix(m.Name)}Serializer.Descriptor,");
        }
        sb.AppendLine("        };");
        sb.AppendLine();

        sb.AppendLine("    public override global::Starlight.Protobuf.Core.MessageDescriptor? GetDescriptor(int cmdId) => cmdId switch");
        sb.AppendLine("    {");

        foreach (var m in messages.Where(m => Cmd(m).HasValue))
        {
            sb.AppendLine($"        {Cmd(m)!.Value} => {CodeEmitter.StripPrefix(m.Name)}Serializer.Descriptor,");
        }
        sb.AppendLine("        _ => null,");
        sb.AppendLine("    };");
        sb.AppendLine();

        sb.AppendLine("    public override global::Starlight.Protobuf.Core.MessageDescriptor? GetDescriptor(global::System.Type messageType)");
        sb.AppendLine("    {");

        foreach (var m in messages)
        {
            sb.AppendLine(
                $"        if (messageType == typeof(global::{baseNs}.{CodeEmitter.StripPrefix(m.Name)})) return {CodeEmitter.StripPrefix(m.Name)}Serializer.Descriptor;");
        }
        sb.AppendLine("        return null;");
        sb.AppendLine("    }");
        sb.AppendLine("}");
        return sb.ToString();
    }
}
