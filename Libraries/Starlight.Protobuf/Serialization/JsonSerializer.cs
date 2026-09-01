using Starlight.Protobuf.Core;
using Starlight.Protobuf.Registry;
using System.Collections;
using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using ByteString = Google.Protobuf.ByteString;
using IMessage = Starlight.Protobuf.Core.IMessage;

namespace Starlight.Protobuf.Serialization;

public sealed class JsonSerializationOptions
{
    public static JsonSerializationOptions Default { get; } = new();

    public bool PreserveProtoFieldNames { get; init; }
    public bool EmitDefaultValues { get; init; }
    public bool WriteEnumsAsIntegers { get; init; }
    public bool WriteInt64AsStrings { get; init; } = true;
    public bool WriteIndented { get; init; }
    public int MaxDepth { get; init; } = 64;
}

public static class JsonSerializer
{
    public static JsonObject ToJsonObject(
        IMessage message,
        ProtocolRegistry registry,
        JsonSerializationOptions? options = null
    )
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(registry);

        var descriptor = registry.GetDescriptor(message.GetType())
                         ?? throw new InvalidOperationException(
                             $"Protocol registry '{registry.Version}' has no descriptor for '{message.GetType().FullName}'.");

        return ToJsonObject(message, descriptor, options);
    }

    public static JsonObject ToJsonObject(
        IMessage message,
        MessageDescriptor descriptor,
        JsonSerializationOptions? options = null
    )
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(descriptor);

        options ??= JsonSerializationOptions.Default;
        ValidateOptions(options);
        return WriteMessage(message, descriptor, options, depth: 0);
    }

    public static JsonObject ToJsonObject(
        IDynamicMessage message,
        JsonSerializationOptions? options = null
    )
    {
        ArgumentNullException.ThrowIfNull(message);
        return ToJsonObject(message, message.Descriptor, options);
    }

    public static JsonObject SerializeToObject(
        IMessage message,
        ProtocolRegistry registry,
        JsonSerializationOptions? options = null
    ) =>
        ToJsonObject(message, registry, options);

    public static JsonObject SerializeToObject(
        IMessage message,
        MessageDescriptor descriptor,
        JsonSerializationOptions? options = null
    ) =>
        ToJsonObject(message, descriptor, options);

    public static JsonObject SerializeToObject(
        IDynamicMessage message,
        JsonSerializationOptions? options = null
    ) =>
        ToJsonObject(message, options);

    public static string Serialize(
        IMessage message,
        ProtocolRegistry registry,
        JsonSerializationOptions? options = null
    )
    {
        options ??= JsonSerializationOptions.Default;
        return ToJsonObject(message, registry, options).ToJsonString(CreateJsonOptions(options));
    }

    public static string Serialize(
        IMessage message,
        MessageDescriptor descriptor,
        JsonSerializationOptions? options = null
    )
    {
        options ??= JsonSerializationOptions.Default;
        return ToJsonObject(message, descriptor, options).ToJsonString(CreateJsonOptions(options));
    }

    public static string Serialize(
        IDynamicMessage message,
        JsonSerializationOptions? options = null
    )
    {
        options ??= JsonSerializationOptions.Default;
        return ToJsonObject(message, options).ToJsonString(CreateJsonOptions(options));
    }

    private static JsonObject WriteMessage(
        object message,
        MessageDescriptor descriptor,
        JsonSerializationOptions options,
        int depth
    )
    {
        if (depth >= options.MaxDepth)
            throw new InvalidOperationException(
                $"Maximum protobuf JSON depth of {options.MaxDepth} was exceeded while serializing '{descriptor.Name}'.");

        var json = new JsonObject();

        foreach (var field in descriptor.Fields)
        {
            var name = options.PreserveProtoFieldNames ? field.Name : ToJsonName(field.Name);

            switch (field.Rule)
            {
                case FieldRule.Repeated:
                    WriteRepeated(json, name, descriptor, message, field, options, depth);
                    break;

                case FieldRule.Map:
                    WriteMap(json, name, descriptor, message, field, options, depth);
                    break;

                default:
                    WriteSingle(json, name, descriptor, message, field, options, depth);
                    break;
            }
        }

        return json;
    }

    private static void WriteSingle(
        JsonObject json,
        string name,
        MessageDescriptor descriptor,
        object message,
        FieldDescriptor field,
        JsonSerializationOptions options,
        int depth
    )
    {
        object? value;

        if (field.InOneof)
        {
            if (!descriptor.OneofActive(message, field))
                return;

            value = descriptor.GetOneof(message, field);
        } else
        {
            value = descriptor.GetValue(message, field);

            if (field.Rule == FieldRule.Optional || field.Kind == ProtoKind.Message)
            {
                if (value is null)
                    return;
            } else if (!options.EmitDefaultValues && IsDefault(field.Kind, value))
            {
                return;
            }
        }

        json[name] = WriteValue(value, field.Kind, field.MessageRef, field, options, depth + 1);
    }

    private static void WriteRepeated(
        JsonObject json,
        string name,
        MessageDescriptor descriptor,
        object message,
        FieldDescriptor field,
        JsonSerializationOptions options,
        int depth
    )
    {
        var list = descriptor.GetList(message, field);

        if (list.Count == 0 && !options.EmitDefaultValues)
            return;

        var array = new JsonArray();

        foreach (var value in list)
        {
            array.Add(WriteValue(value, field.Kind, field.MessageRef, field, options, depth + 1));
        }

        json[name] = array;
    }

    private static void WriteMap(
        JsonObject json,
        string name,
        MessageDescriptor descriptor,
        object message,
        FieldDescriptor field,
        JsonSerializationOptions options,
        int depth
    )
    {
        var map = descriptor.GetMap(message, field);

        if (map.Count == 0 && !options.EmitDefaultValues)
            return;

        var obj = new JsonObject();

        foreach (DictionaryEntry entry in map)
        {
            var key = MapKeyToString(entry.Key, field.KeyKind);
            obj[key] = WriteValue(entry.Value, field.Kind, field.MessageRef, field, options, depth + 1);
        }

        json[name] = obj;
    }

    private static JsonNode? WriteValue(
        object? value,
        ProtoKind kind,
        Func<MessageDescriptor>? messageRef,
        FieldDescriptor field,
        JsonSerializationOptions options,
        int depth
    )
    {
        if (value is null)
            return null;

        if (kind == ProtoKind.Message)
        {
            var descriptor = messageRef?.Invoke()
                             ?? throw new InvalidOperationException($"Message field '{field.Name}' has no nested descriptor.");

            return WriteMessage(value, descriptor, options, depth);
        }

        return kind switch {
            ProtoKind.Double => WriteDouble(Convert.ToDouble(value, CultureInfo.InvariantCulture)),
            ProtoKind.Float => WriteFloat(Convert.ToSingle(value, CultureInfo.InvariantCulture)),
            ProtoKind.Int32 or ProtoKind.SInt32 or ProtoKind.SFixed32 =>
                JsonValue.Create(Convert.ToInt32(value, CultureInfo.InvariantCulture)),
            ProtoKind.UInt32 or ProtoKind.Fixed32 => JsonValue.Create(Convert.ToUInt32(value, CultureInfo.InvariantCulture)),
            ProtoKind.Int64 or ProtoKind.SInt64 or ProtoKind.SFixed64 => WriteInt64(Convert.ToInt64(value, CultureInfo.InvariantCulture),
                options),
            ProtoKind.UInt64 or ProtoKind.Fixed64 => WriteUInt64(Convert.ToUInt64(value, CultureInfo.InvariantCulture), options),
            ProtoKind.Bool => JsonValue.Create(Convert.ToBoolean(value, CultureInfo.InvariantCulture)),
            ProtoKind.String => JsonValue.Create((string)value),
            ProtoKind.Bytes => JsonValue.Create(Convert.ToBase64String(((ByteString)value).ToByteArray())),
            ProtoKind.Enum => WriteEnum(value, field, options),
            _ => throw new InvalidOperationException($"Unsupported protobuf kind '{kind}'.")
        };
    }

    private static JsonNode WriteEnum(object value, FieldDescriptor field, JsonSerializationOptions options)
    {
        var number = Convert.ToInt32(value, CultureInfo.InvariantCulture);

        if (options.WriteEnumsAsIntegers)
            return JsonValue.Create(number)!;

        var enumType = ResolveEnumType(field);

        if (enumType is not null)
        {
            var name = Enum.GetName(enumType, number);

            if (name is not null)
                return JsonValue.Create(name)!;
        }

        return JsonValue.Create(number)!;
    }

    private static Type? ResolveEnumType(FieldDescriptor field)
    {
        var type = field.Property?.PropertyType;

        if (type is null)
            return null;

        if (field.Rule == FieldRule.Repeated)
        {
            if (!type.IsGenericType)
                return null;

            type = type.GetGenericArguments()[0];
        } else if (field.Rule == FieldRule.Map)
        {
            if (!type.IsGenericType)
                return null;

            type = type.GetGenericArguments()[1];
        }

        type = Nullable.GetUnderlyingType(type) ?? type;
        return type.IsEnum ? type : null;
    }

    private static JsonNode WriteInt64(long value, JsonSerializationOptions options) =>
        options.WriteInt64AsStrings ? JsonValue.Create(value.ToString(CultureInfo.InvariantCulture))! : JsonValue.Create(value)!;

    private static JsonNode WriteUInt64(ulong value, JsonSerializationOptions options) =>
        options.WriteInt64AsStrings ? JsonValue.Create(value.ToString(CultureInfo.InvariantCulture))! : JsonValue.Create(value)!;

    private static JsonNode WriteFloat(float value)
    {
        if (float.IsNaN(value)) return JsonValue.Create("NaN")!;
        if (float.IsPositiveInfinity(value)) return JsonValue.Create("Infinity")!;
        if (float.IsNegativeInfinity(value)) return JsonValue.Create("-Infinity")!;

        return JsonValue.Create(value)!;
    }

    private static JsonNode WriteDouble(double value)
    {
        if (double.IsNaN(value)) return JsonValue.Create("NaN")!;
        if (double.IsPositiveInfinity(value)) return JsonValue.Create("Infinity")!;
        if (double.IsNegativeInfinity(value)) return JsonValue.Create("-Infinity")!;

        return JsonValue.Create(value)!;
    }

    private static string MapKeyToString(object? value, ProtoKind kind)
    {
        if (value is null)
            return "";

        return kind switch {
            ProtoKind.Bool => Convert.ToBoolean(value, CultureInfo.InvariantCulture) ? "true" : "false",
            ProtoKind.String => (string)value,
            ProtoKind.Int32 or ProtoKind.SInt32 or ProtoKind.SFixed32 => Convert.ToInt32(value, CultureInfo.InvariantCulture)
                .ToString(CultureInfo.InvariantCulture),
            ProtoKind.UInt32 or ProtoKind.Fixed32 => Convert.ToUInt32(value, CultureInfo.InvariantCulture)
                .ToString(CultureInfo.InvariantCulture),
            ProtoKind.Int64 or ProtoKind.SInt64 or ProtoKind.SFixed64 => Convert.ToInt64(value, CultureInfo.InvariantCulture)
                .ToString(CultureInfo.InvariantCulture),
            ProtoKind.UInt64 or ProtoKind.Fixed64 => Convert.ToUInt64(value, CultureInfo.InvariantCulture)
                .ToString(CultureInfo.InvariantCulture),
            _ => throw new InvalidOperationException($"Protobuf map key kind '{kind}' is not supported by JSON.")
        };
    }

    private static bool IsDefault(ProtoKind kind, object? value)
    {
        if (value is null)
            return true;

        return kind switch {
            ProtoKind.String => ((string)value).Length == 0,
            ProtoKind.Bytes => ((ByteString)value).Length == 0,
            ProtoKind.Bool => !Convert.ToBoolean(value, CultureInfo.InvariantCulture),
            ProtoKind.Float => Convert.ToSingle(value, CultureInfo.InvariantCulture) == 0f,
            ProtoKind.Double => Convert.ToDouble(value, CultureInfo.InvariantCulture) == 0d,
            ProtoKind.Int64 or ProtoKind.SInt64 or ProtoKind.SFixed64 => Convert.ToInt64(value, CultureInfo.InvariantCulture) == 0L,
            ProtoKind.UInt32 or ProtoKind.Fixed32 => Convert.ToUInt32(value, CultureInfo.InvariantCulture) == 0u,
            ProtoKind.UInt64 or ProtoKind.Fixed64 => Convert.ToUInt64(value, CultureInfo.InvariantCulture) == 0UL,
            _ => Convert.ToInt64(value, CultureInfo.InvariantCulture) == 0L
        };
    }

    private static string ToJsonName(string protoName)
    {
        var underscore = protoName.IndexOf('_');

        if (underscore < 0)
            return protoName;

        var builder = new StringBuilder(protoName.Length);
        var upper = false;

        foreach (var c in protoName)
        {
            if (c == '_')
            {
                upper = true;
                continue;
            }

            builder.Append(upper ? char.ToUpperInvariant(c) : c);
            upper = false;
        }

        return builder.ToString();
    }

    private static System.Text.Json.JsonSerializerOptions CreateJsonOptions(JsonSerializationOptions options) =>
        new() { WriteIndented = options.WriteIndented };

    private static void ValidateOptions(JsonSerializationOptions options)
    {
        if (options.MaxDepth <= 0)
            throw new ArgumentOutOfRangeException(nameof(options.MaxDepth), options.MaxDepth, "MaxDepth must be greater than zero.");
    }
}
