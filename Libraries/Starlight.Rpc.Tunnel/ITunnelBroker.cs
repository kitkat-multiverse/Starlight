namespace Starlight.Rpc.Tunnel;

/// <summary>
/// Holds one end of a pending in-process tunnel until the requester claims it.
/// </summary>
public interface ITunnelBroker
{
    /// <summary>Stashes <paramref name="clientEnd"/> and returns a handle to share with the requester.</summary>
    Guid Register(RpcTunnel clientEnd);

    /// <summary>Claims and removes the end registered under <paramref name="handle"/>. Returns null if the handle is unknown.</summary>
    RpcTunnel? Claim(Guid handle);
}
