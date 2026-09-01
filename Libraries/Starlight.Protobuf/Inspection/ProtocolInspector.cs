using Google.Protobuf;
using Starlight.Protobuf.Core;
using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;
using System.Text;
using System.Text.Json;
using IMessage = Starlight.Protobuf.Core.IMessage;

namespace Starlight.Protobuf.Inspection;

/// <summary>
/// Renders a message as JSON for traffic inspection: known properties plus any
/// unmatched/obfuscated wire fields captured during deserialization. Used by the
/// traffic visualizer, which can run per-packet on a live server, so the
/// reflection cost of property discovery is cached per type. This is separate
/// from the serialization fast path.
/// </summary>
public static class ProtocolInspector
{
    public static string ToJson(IMessage message, bool indented = false)
    {
        using var stream = new MemoryStream();

        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = indented }))
        {
            WriteMessage(writer, message);
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteMessage(Utf8JsonWriter writer, IMessage message)
    {
        writer.WriteStartObject();

        if (message is IDynamicMessage dynamic)
            WriteDynamicFields(writer, dynamic);
        else
            WritePocoFields(writer, message);

        if (message.UnknownFields is { IsEmpty: false } unknown)
        {
            writer.WritePropertyName("_unknown");
            writer.WriteStartArray();

            foreach (var field in unknown.Fields)
            {
                writer.WriteStartObject();
                writer.WriteNumber("field", field.FieldNumber);
                writer.WriteString("wireType", field.WireType.ToString());
                writer.WriteString("data", Convert.ToBase64String(field.Data));
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        }

        writer.WriteEndObject();
    }

    private static readonly ConcurrentDictionary<Type, PropertyInfo[]> PocoProperties = new();

    private static void WritePocoFields(Utf8JsonWriter writer, IMessage message)
    {
        var props = PocoProperties.GetOrAdd(message.GetType(), static type =>
            type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.Name != nameof(IMessage.UnknownFields) && p.GetIndexParameters().Length == 0)
                .ToArray());

        foreach (var prop in props)
        {
            writer.WritePropertyName(CamelCase(prop.Name));
            WriteValue(writer, prop.GetValue(message));
        }
    }

    /// <summary>Renders a dynamic (CLR-typeless) message by walking its descriptor.</summary>
    private static void WriteDynamicFields(Utf8JsonWriter writer, IDynamicMessage message)
    {
        var desc = message.Descriptor;

        foreach (var f in desc.Fields)
        {
            object? value;

            switch (f.Rule)
            {
                case FieldRule.Map:
                    value = desc.GetMap(message, f);
                    break;
                case FieldRule.Repeated:
                    value = desc.GetList(message, f);
                    break;
                default:
                    if (f.InOneof)
                    {
                        if (!desc.OneofActive(message, f)) continue;

                        value = desc.GetOneof(message, f);
                    } else
                    {
                        value = desc.GetValue(message, f);
                    }

                    break;
            }

            writer.WritePropertyName(CamelCase(Pascal(f.Name)));
            WriteValue(writer, value);
        }
    }

    private static string Pascal(string snake)
    {
        var sb = new StringBuilder(snake.Length);
        var upper = false;

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

    private static void WriteValue(Utf8JsonWriter writer, object? value)
    {
        switch (value)
        {
            case null:
                writer.WriteNullValue();
                break;
            case IMessage m:
                WriteMessage(writer, m);
                break;
            case string s:
                writer.WriteStringValue(s);
                break;
            case bool b:
                writer.WriteBooleanValue(b);
                break;
            case ByteString bs:
                writer.WriteStringValue(Convert.ToBase64String(bs.ToByteArray()));
                break;
            case Enum e:
                writer.WriteStringValue(e.ToString());
                break;
            case sbyte sb:
                writer.WriteNumberValue(sb);
                break;
            case byte by:
                writer.WriteNumberValue(by);
                break;
            case short sh:
                writer.WriteNumberValue(sh);
                break;
            case ushort us:
                writer.WriteNumberValue(us);
                break;
            case int i:
                writer.WriteNumberValue(i);
                break;
            case uint ui:
                writer.WriteNumberValue(ui);
                break;
            case long l:
                writer.WriteNumberValue(l);
                break;
            case ulong ul:
                writer.WriteNumberValue(ul);
                break;
            case float f:
                writer.WriteNumberValue(f);
                break;
            case double d:
                writer.WriteNumberValue(d);
                break;
            case decimal dec:
                writer.WriteNumberValue(dec);
                break;
            case IDictionary dict:
                writer.WriteStartObject();

                foreach (DictionaryEntry entry in dict)
                {
                    writer.WritePropertyName(entry.Key?.ToString() ?? "");
                    WriteValue(writer, entry.Value);
                }

                writer.WriteEndObject();
                break;
            case IEnumerable enumerable:
                writer.WriteStartArray();

                foreach (var item in enumerable)
                {
                    WriteValue(writer, item);
                }
                writer.WriteEndArray();
                break;
            default:
                writer.WriteStringValue(value.ToString());
                break;
        }
    }

    private static string CamelCase(string name) =>
        name.Length == 0 || char.IsLower(name[0]) ? name : char.ToLowerInvariant(name[0]) + name.Substring(1);
}
