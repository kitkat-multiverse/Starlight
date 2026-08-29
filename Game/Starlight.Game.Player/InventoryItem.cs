using Starlight.Protocol;

namespace Starlight.Game.Player;

public abstract class InventoryItem
{
    public required uint ItemId { get; init; }
    public required ulong Guid { get; init; }

    public abstract Item ToProtocol();
}

public sealed class MaterialItem : InventoryItem
{
    public uint Count { get; internal set; }

    public override Item ToProtocol() => new() {
        ItemId = ItemId,
        Guid = Guid,
        Material = new Material { Count = Count }
    };
}
