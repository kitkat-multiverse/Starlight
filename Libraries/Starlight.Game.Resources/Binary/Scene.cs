using System.Text.Json.Serialization;

namespace Starlight.Game.Resources.Binary;

public sealed class ScenePointConfig
{
    public Dictionary<string, PointData> Points { get; set; } = new();
}

public sealed class PointData
{
    public uint PointId { get; set; }
    public uint SceneId { get; set; }

    public uint AreaId { get; set; }
    public uint GadgetId { get; set; }

    public string MarkIconTypeName { get; set; } = string.Empty;
    [JsonPropertyName("$type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("pos")] public Position PointPos { get; set; } = new();
    [JsonPropertyName("tranPos")] public Position TeleportPos { get; set; } = new();
    [JsonPropertyName("dungeonIds")] public List<uint> DungeonIds { get; set; } = [];
    [JsonPropertyName("tranSceneId")] public uint TranSceneId { get; set; }
}
