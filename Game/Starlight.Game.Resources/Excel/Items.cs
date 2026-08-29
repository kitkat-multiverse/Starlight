using System.Text.Json.Serialization;

namespace Starlight.Game.Resources.Excel;

[GameResource("WeaponExcelConfigData.json")]
public sealed class WeaponData : Data
{
    [JsonPropertyName("id")]
    public new uint Id { get; set; }

    [JsonPropertyName("gadgetId")]
    public uint GadgetId { get; set; }

    /// The passive affix carried by this weapon. It's map value is refinement - 1.
    [JsonPropertyName("skillAffix")]
    public List<uint> SkillAffix { get; set; } = [];
}

[GameResource("MaterialExcelConfigData.json")]
public sealed class MaterialData : Data
{
    [JsonPropertyName("id")]
    public new uint Id { get; set; }

    [JsonPropertyName("stackLimit")]
    public uint StackLimit { get; set; } = 1;

    [JsonPropertyName("itemType")]
    public string ItemType { get; set; } = "ITEM_MATERIAL";

    [JsonPropertyName("materialType")]
    public string MaterialType { get; set; } = string.Empty;

    [JsonPropertyName("useOnGain")]
    public bool UseOnGain { get; set; }

    public bool IsInventoryMaterial => ItemType == "ITEM_MATERIAL" && !UseOnGain;
}
