namespace Starlight.Protocol;

/// <summary>
/// Marks a method as a packet handler.
/// <br/>
/// A source generator takes all methods annotated with this attribute and
/// compiles into a switch statement corresponding the message type (substitute for <c>CmdId</c>)
/// to the appropriate packet handler.
/// <br/>
/// For modules looking to start a routine after receiving a packet, look into <see cref="LifecycleEvent"/> instead.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class OpcodeAttribute : Attribute
{
    /// <summary>Infers the handled message type from the method's message-typed parameter.</summary>
    public OpcodeAttribute()
    {}

    /// <summary>Explicitly sets the handled message type, for handlers without a message parameter.</summary>
    public OpcodeAttribute(Type message)
    {
        Message = message;
    }

    /// <summary>The protocol message type this handler is invoked for, or <c>null</c> to infer it.</summary>
    public Type? Message { get; }
}
