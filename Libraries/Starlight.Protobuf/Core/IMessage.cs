namespace Starlight.Protobuf.Core;

/// <summary>
/// Slim marker for a protocol message. Messages are pure POCOs; all
/// (de)serialization lives in an external <see cref="ISerializer{T}"/>.
/// </summary>
public interface IMessage
{
    /// <summary>
    /// Fields seen on the wire during deserialization that had no matching
    /// base field (obfuscated / unknown). Captured for inspection, never
    /// re-emitted on serialize. <c>null</c> until the first unknown field is
    /// captured, so the common case allocates nothing.
    /// </summary>
    UnknownFieldSet? UnknownFields { get; set; }
}

/// <summary>
/// Self-typed message marker. The type parameter exists so generated code and
/// extension methods can recover the concrete message type.
/// </summary>
public interface IMessage<T> : IMessage where T : IMessage<T>;

/// <summary>
/// A message that owns a canonical serializer reachable with no runtime lookup,
/// powering the argument-free <c>ToByteArray()</c> / <c>MergeFrom(byte[])</c> extensions.
/// Implemented by version-independent messages (<c>extra.proto</c>), which have exactly
/// one serializer, and by base messages, whose canonical serializer encodes with the
/// structural base field numbers -- a lossless wire format for server-to-server exchange.
/// Encoding a base message for a specific protocol version instead uses the explicit
/// per-version <see cref="ISerializer{T}"/> overload.
/// </summary>
public interface ISelfSerializable<T> : IMessage<T> where T : ISelfSerializable<T>
{
    /// <summary>The one serializer for this message type.</summary>
    abstract static ISerializer<T> Serializer { get; }
}
