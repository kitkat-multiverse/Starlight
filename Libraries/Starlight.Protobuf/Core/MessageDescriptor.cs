using System.Collections;
using System.Collections.Frozen;
using System.Reflection;
using Starlight.Protobuf.Inspection;
using Starlight.Protobuf.Serialization;

namespace Starlight.Protobuf.Core;

/// <summary>Proto3 scalar/aggregate kind for a field, off the hot path.</summary>
public enum ProtoKind
{
    Double,
    Float,
    Int32,
    Int64,
    UInt32,
    UInt64,
    SInt32,
    SInt64,
    Fixed32,
    Fixed64,
    SFixed32,
    SFixed64,
    Bool,
    String,
    Bytes,
    Enum,
    Message
}

/// <summary>How a field repeats / carries presence.</summary>
public enum FieldRule
{
    Single,
    Optional,
    Repeated,
    Map
}

/// <summary>
/// Descriptor-driven access to a message's fields. Implemented by
/// <c>DynamicMessage</c> (reflection registry) so the shared
/// <see cref="ReflectiveEngine"/> can (de)serialize a property bag with no CLR type.
/// </summary>
public interface IDynamicAccessor
{
    object? Get(string field);
    void Set(string field, object? value);
    IList GetList(string field);
    IDictionary GetMap(string field);
    int ActiveOneof(string oneofName);
    object? GetOneof(string oneofName);
    void SetOneof(string oneofName, int caseNumber, object? value);
}

/// <summary>
/// A message with no CLR type, carrying its own <see cref="MessageDescriptor"/>
/// (the reflection registry's <c>DynamicMessage</c>). Lets descriptor-driven tools
/// (e.g. <see cref="ProtocolInspector"/>) render dynamic messages without a POCO.
/// </summary>
public interface IDynamicMessage : IMessage, IDynamicAccessor
{
    MessageDescriptor Descriptor { get; }
}

/// <summary>
/// Field table entry powering the reflective / remap / reflection-registry paths
/// only. The hardcoded fast path never touches this.
/// </summary>
public sealed class FieldDescriptor
{
    public FieldDescriptor(
        string name,
        string propertyName,
        int baseNumber,
        int wireNumber,
        ProtoKind kind,
        FieldRule rule,
        string? oneofName = null,
        ProtoKind keyKind = ProtoKind.Int32,
        Func<MessageDescriptor>? messageRef = null,
        FieldTransform? transform = null
    )
    {
        Name = name;
        PropertyName = propertyName;
        BaseNumber = baseNumber;
        DefaultNumber = wireNumber;
        Number = wireNumber;
        Kind = kind;
        Rule = rule;
        OneofName = oneofName;
        KeyKind = keyKind;
        MessageRef = messageRef;
        Transform = transform;
    }

    /// <summary>Canonical proto field name.</summary>
    public string Name { get; }

    /// <summary>C# property name on the POCO (collision-suffixed to match protoc).</summary>
    public string PropertyName { get; }

    /// <summary>Structural base field number (oneof discriminator value).</summary>
    public int BaseNumber { get; }

    /// <summary>Version wire field number before any remap.</summary>
    public int DefaultNumber { get; }

    /// <summary>Effective wire field number (== <see cref="DefaultNumber"/> unless remapped).</summary>
    public int Number { get; internal set; }

    public ProtoKind Kind { get; }
    public FieldRule Rule { get; }
    public bool InOneof => OneofName is not null;
    public string? OneofName { get; }

    /// <summary>Key kind for a map field.</summary>
    public ProtoKind KeyKind { get; }

    /// <summary>Nested message descriptor for message / map-value-message / repeated-message fields.</summary>
    public Func<MessageDescriptor>? MessageRef { get; }

    /// <summary>Wire-value obfuscation for a single scalar integer field, or <c>null</c> if none.</summary>
    public FieldTransform? Transform { get; }

    // -- resolved reflection accessors (compiled POCOs only) ------------------
    internal PropertyInfo? Property;
    internal PropertyInfo? CaseProperty;
}

/// <summary>
/// Field table for one message in one version. Built by generated code (compiled
/// POCOs) or by the reflection registry (dynamic messages). Drives the shared
/// <see cref="ReflectiveEngine"/> and the opt-in field-ID remap.
/// </summary>
public sealed class MessageDescriptor
{
    /// <summary>
    /// Immutable snapshot of the remap state: the branch-once gate plus the
    /// number→field index that matches it. Published as a unit through
    /// <see cref="_index"/> so readers never observe a half-built index or a
    /// gate that disagrees with the table it guards (copy-on-write).
    /// </summary>
    private sealed class Index
    {
        public Index(bool hasRemaps, FrozenDictionary<int, FieldDescriptor> byNumber)
        {
            HasRemaps = hasRemaps;
            ByNumber = byNumber;
        }

        public bool HasRemaps { get; }
        public FrozenDictionary<int, FieldDescriptor> ByNumber { get; }
    }

    /// <summary>
    /// Current remap snapshot. <c>volatile</c> makes every publish visible to
    /// hot-path readers with no lock; writers swap a fully-built replacement.
    /// </summary>
    private volatile Index _index;

    /// <summary>Serializes the rare write events (<see cref="Remap"/> / <see cref="ClearRemaps"/>) against each other.</summary>
    private readonly object _remapLock = new();

    public MessageDescriptor(string name, Type? clrType, IReadOnlyList<FieldDescriptor> fields, Func<object>? factory = null)
    {
        Name = name;
        ClrType = clrType;
        Fields = fields;
        Factory = factory ?? (clrType is not null ? () => Activator.CreateInstance(clrType)! : null);

        if (clrType is not null)
        {
            foreach (var f in fields)
            {
                f.Property = clrType.GetProperty(f.PropertyName);

                if (f.OneofName is not null)
                    f.CaseProperty = clrType.GetProperty(f.OneofName + "Case");
            }
        }

        _index = BuildIndex(hasRemaps: false);
    }

    public string Name { get; }

    /// <summary>POCO type for compiled messages; <c>null</c> for dynamic ones.</summary>
    public Type? ClrType { get; }

    public IReadOnlyList<FieldDescriptor> Fields { get; }

    /// <summary>Creates an empty instance (POCO or dynamic message).</summary>
    public Func<object>? Factory { get; }

    /// <summary>True once any field has been remapped; the branch-once gate for the fast path.</summary>
    public bool HasRemaps => _index.HasRemaps;

    /// <summary>Looks up a field by its effective wire number.</summary>
    public FieldDescriptor? FindByNumber(int number) => _index.ByNumber.TryGetValue(number, out var f) ? f : null;

    /// <summary>Looks up a field by canonical name or C# property name.</summary>
    public FieldDescriptor? Find(string nameOrProperty) =>
        Fields.FirstOrDefault(f => f.Name == nameOrProperty || f.PropertyName == nameOrProperty);

    /// <summary>
    /// Overrides the effective wire number of a field (live deobfuscation). Flips the
    /// message onto the reflective slow path; the fast path resumes after
    /// <see cref="ClearRemaps"/>. Returns false if no such field exists.
    ///
    /// Thread-safe against concurrent (de)serialization and other writers: the new
    /// number and matching index are published atomically as one immutable snapshot.
    /// A single <c>Remap</c> is fully consistent for in-flight readers; if you remap
    /// several fields that must take effect together, quiesce serialization first,
    /// since each call publishes independently.
    /// </summary>
    public bool Remap(string fieldOrProperty, int wireNumber)
    {
        lock (_remapLock)
        {
            var f = Find(fieldOrProperty);
            if (f is null) return false;

            f.Number = wireNumber;
            _index = BuildIndex(hasRemaps: true);
            return true;
        }
    }

    /// <summary>Restores every field's default wire number and returns to the fast path.</summary>
    public void ClearRemaps()
    {
        lock (_remapLock)
        {
            foreach (var f in Fields)
            {
                f.Number = f.DefaultNumber;
            }
            _index = BuildIndex(hasRemaps: false);
        }
    }

    /// <summary>Builds a fresh, frozen number→field index from the current effective numbers.</summary>
    private Index BuildIndex(bool hasRemaps)
    {
        var byNumber = new Dictionary<int, FieldDescriptor>(Fields.Count);

        foreach (var f in Fields)
        {
            if (byNumber.TryGetValue(f.Number, out var existing))
            {
                throw new InvalidOperationException(
                    $"Duplicate wire field number {f.Number} on message '{Name}' for fields '{existing.Name}' and '{f.Name}'.");
            }
            byNumber[f.Number] = f;
        }
        return new Index(hasRemaps, byNumber.ToFrozenDictionary());
    }

    // -- value access (reflection for POCOs, accessor for dynamic) -----------

    internal object? GetValue(object msg, FieldDescriptor f)
    {
        if (ClrType is null)
            return Normalize(f.Kind, ((IDynamicAccessor)msg).Get(f.Name));

        var raw = f.Property!.GetValue(msg);
        return Normalize(f.Kind, raw);
    }

    internal void SetValue(object msg, FieldDescriptor f, object? value)
    {
        if (ClrType is null)
            ((IDynamicAccessor)msg).Set(f.Name, value);
        else
            f.Property!.SetValue(msg, Coerce(f.Property!.PropertyType, value));
    }

    internal IList GetList(object msg, FieldDescriptor f) =>
        ClrType is null ? ((IDynamicAccessor)msg).GetList(f.Name) : (IList)f.Property!.GetValue(msg)!;

    internal IDictionary GetMap(object msg, FieldDescriptor f) =>
        ClrType is null ? ((IDynamicAccessor)msg).GetMap(f.Name) : (IDictionary)f.Property!.GetValue(msg)!;

    internal bool OneofActive(object msg, FieldDescriptor f)
    {
        if (ClrType is null)
            return ((IDynamicAccessor)msg).ActiveOneof(f.OneofName!) == f.BaseNumber;

        var c = f.CaseProperty!.GetValue(msg);
        return c is not null && Convert.ToInt32(c) == f.BaseNumber;
    }

    internal object? GetOneof(object msg, FieldDescriptor f)
    {
        if (ClrType is null)
            return Normalize(f.Kind, ((IDynamicAccessor)msg).GetOneof(f.OneofName!));

        return Normalize(f.Kind, f.Property!.GetValue(msg));
    }

    internal void SetOneof(object msg, FieldDescriptor f, object? value)
    {
        if (ClrType is null)
            ((IDynamicAccessor)msg).SetOneof(f.OneofName!, f.BaseNumber, value);
        else
            f.Property!.SetValue(msg, Coerce(f.Property!.PropertyType, value));
    }

    internal object NewElement(FieldDescriptor f) => f.MessageRef!().Factory!();

    /// <summary>Adds an element to a repeated field, coercing to the list element type.</summary>
    internal void AddElement(IList list, FieldDescriptor f, object? value)
    {
        if (ClrType is null)
        {
            list.Add(value);
            return;
        }
        var elemType = list.GetType().GetGenericArguments()[0];
        list.Add(Coerce(elemType, value));
    }

    /// <summary>Puts a key/value pair into a map field, coercing to its CLR types.</summary>
    internal void PutEntry(IDictionary map, object? key, object? value)
    {
        if (ClrType is null)
        {
            map[key!] = value;
            return;
        }
        var args = map.GetType().GetGenericArguments();
        map[Coerce(args[0], key)!] = Coerce(args[1], value);
    }

    private static object? Normalize(ProtoKind kind, object? raw)
    {
        if (raw is null) return null;

        return kind == ProtoKind.Enum ? Convert.ToInt32(raw) : raw;
    }

    private static object? Coerce(Type target, object? value)
    {
        if (value is null) return null;

        var underlying = Nullable.GetUnderlyingType(target) ?? target;
        return underlying.IsEnum ? Enum.ToObject(underlying, Convert.ToInt64(value)) : value;
    }
}
