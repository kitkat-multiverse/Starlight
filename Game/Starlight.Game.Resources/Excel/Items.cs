using System.Text.Json.Serialization;

namespace Starlight.Game.Resources.Excel;

[GameResource("WeaponExcelConfigData.json")]
public sealed class WeaponData : Data
{
    [JsonPropertyName("id")]
    public new uint Id { get; set; }

    [JsonPropertyName("gadgetId")]
    public uint GadgetId { get; set; }
}
