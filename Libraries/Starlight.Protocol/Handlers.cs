namespace Starlight.Protocol;

/// <summary>
/// Marks a method as a packet handler for the message type <paramref name="message"/>.
/// The <see cref="Starlight.CodeGen.Network.PacketHandlerGenerator"/> wires matching
/// methods into a generated switch keyed on the deserialized message's runtime type.
/// <br/>
/// A single message type may have multiple handlers; they run in order of descending
/// <see cref="Priority"/>, ties broken alphabetically by method name.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class OpcodeAttribute(Type message) : Attribute
{
    /// <summary>The protocol message type this handler is invoked for.</summary>
    public Type Message { get; } = message;

    /// <summary>
    /// Relative run order when several handlers share a message type; higher runs first.
    /// Defaults to <c>0</c>. Handlers with equal priority run alphabetically by method name.
    /// </summary>
    public int Priority { get; init; }
}
