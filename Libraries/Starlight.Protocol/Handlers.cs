namespace Starlight.Protocol;

/// <summary>
/// Marks a method as a packet handler. The
/// <see cref="Starlight.CodeGen.Network.PacketHandlerGenerator"/> wires matching methods into
/// a generated switch keyed on the deserialized message's runtime type.
/// <br/>
/// The handled message type is inferred from the method's message-typed parameter, so
/// <c>[Opcode]</c> suffices for <c>OnFoo(FooReq msg)</c>. Pass it explicitly with
/// <c>[Opcode(typeof(FooReq))]</c> only when the handler has no message parameter to infer
/// from; a handler with neither is a compile error.
/// <br/>
/// A single message type may have multiple handlers; they run in order of descending
/// <see cref="Priority"/>, ties broken alphabetically by method name.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class OpcodeAttribute : Attribute
{
    /// <summary>Infers the handled message type from the method's message-typed parameter.</summary>
    public OpcodeAttribute() { }

    /// <summary>Explicitly sets the handled message type, for handlers without a message parameter.</summary>
    public OpcodeAttribute(Type message) => Message = message;

    /// <summary>The protocol message type this handler is invoked for, or <c>null</c> to infer it.</summary>
    public Type? Message { get; }

    /// <summary>
    /// Relative run order when several handlers share a message type; higher runs first.
    /// Defaults to <c>0</c>. Handlers with equal priority run alphabetically by method name.
    /// </summary>
    public int Priority { get; init; }
}
