namespace Starlight.Gate;

public sealed class GateConfig
{
    /// All gateway servers must belong to a region, in which they all agree
    /// upon the values in the <see cref="RegionConfig"/>.
    public RegionConfig Region { get; set; } = new();

    public string BindAddress { get; set; } = "0.0.0.0";
    public ushort BindPort { get; set; } = 22102;

    /// When enabled, the gate server will report a *localhost* address
    /// instead of the public IP address.
    public bool ServingLocal { get; set; }

    public ushort ServePort { get; set; } = 22102;
}

public sealed class RegionConfig
{
    /// The internal identifier used by the dispatch server for denoting
    /// this region.
    public string Identifier { get; set; } = "sl_local";

    /// The name of the region this gateway belongs to.
    /// <br/>
    /// This is the name which shows up in-game.
    public string DisplayName { get; set; } = "Starlight (local)";
}

