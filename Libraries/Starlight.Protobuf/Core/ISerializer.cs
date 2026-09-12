using Google.Protobuf;

namespace Starlight.Protobuf.Core;

/// <summary>
///     Per-version, hardcoded (de)serializer for a single message type. One instance
///     exists per <c>(message, version)</c> pair; instances are stateless and shared.
/// </summary>
/// <remarks>
///     The wire surface is <see cref="CodedOutputStream" /> / <see cref="CodedInputStream" />.
///     The span-based <c>WriteContext</c>/<c>ParseContext</c> path was evaluated but its
///     construction is internal to Google.Protobuf; the <c>Coded*</c> streams expose the
///     same method surface and are used instead.
/// </remarks>
public interface ISerializer<in T> where T : IMessage
{
    /// <summary>Exact number of bytes <paramref name="message" /> serializes to.</summary>
    int CalculateSize(T message);

    /// <summary>Writes <paramref name="message" /> to <paramref name="output" /> using this version's field IDs.</summary>
    void Serialize(T message, CodedOutputStream output);

    /// <summary>Reads fields from <paramref name="input" /> into <paramref name="message" /> using this version's field IDs.</summary>
    void Deserialize(T message, CodedInputStream input);
}
