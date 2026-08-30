namespace Starlight.Protocol;

/// <summary>
/// A substitute for <see cref="OpcodeAttribute"/>.
/// <br/>
/// Marks a method as a <b>lifecycle event handler</b>.
/// See <see cref="LifecycleEvent"/> for all possible events.
/// <br/>
/// Handlers take no message, only the session player. Anything they return is sent to the client.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class LifecycleAttribute(LifecycleEvent @event, LifecycleOrder order = LifecycleOrder.Normal) : Attribute
{
    public LifecycleEvent Event => @event;
    public LifecycleOrder Order => order;
}

public enum LifecycleEvent
{
    /// Sent once <c>PlayerLoginReq</c> has loaded the player's data, before <c>PlayerLoginRsp</c> goes out.
    PlayerLogin,
    /// Sent after a new player has selected their Traveler and nickname.
    PlayerBorn,
    /// Sent when the KCP session is dropped. The tunnel is gone by now, so sends are discarded.
    PlayerDisconnect,
    /// Sent when the player data is being saved to the database, before a PlayerDisconnect.
    PlayerSaving
}

public enum LifecycleOrder
{
    First = -1000,
    HighPriority = -500,
    Normal = 0,
    LowPriority = 500,
    Last = 1000
}
