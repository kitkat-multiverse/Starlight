using System.Text.Json.Serialization;

namespace Starlight.Game.Resources.Excel;

[GameResource("CoopPointExcelConfigData.json", Priority = LoadPriority.Low)]
public sealed class CoopPointData : Data
{
    [JsonPropertyName("chapterId")]
    public uint ChapterId { get; set; }
    [JsonPropertyName("acceptQuest")]
    public uint AcceptQuest { get; set; }
    [JsonPropertyName("postPointList")]
    public uint[] PostPointList { get; set; } = [];
}
