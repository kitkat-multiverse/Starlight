namespace Starlight.Gate;

public sealed class GateConfig
{
    public string ServerId { get; set; } = "localhost";
    public string RegionId { get; set; } = "sl_local";

    public string BindAddress { get; set; } = "0.0.0.0";
    public ushort BindPort { get; set; } = 22102;

    /// When enabled, the gate server will report a *localhost* address
    /// instead of the public IP address.
    public bool ServingLocal { get; set; }

    public ushort ServePort { get; set; } = 22102;

    public ConnectionConfig Connections { get; set; } = new();
}

public sealed class ConnectionConfig
{
    /// Adds a debug log which logs:
    /// <ul>
    ///     <li>the client's remote address</li>
    ///     <li>the packet's ID</li>
    ///     <li>the packet's size</li>
    /// </ul>
    public bool LogPackets { get; set; } = false;
}
