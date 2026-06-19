namespace Starlight.Gate;

public sealed class GateConfig
{
    public string BindAddress { get; set; } = "0.0.0.0";
    public ushort BindPort { get; set; } = 22102;

    /// When enabled, the gate server will report a *localhost* address
    /// instead of the public IP address.
    public bool ServingLocal { get; set; }

    public ushort ServePort { get; set; } = 22102;
}

