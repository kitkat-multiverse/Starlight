using System.Text.Json.Serialization;

namespace Starlight.Game.Resources.Excel;

[GameResource("WeaponExcelConfigData.json")]
public sealed class WeaponData : Data
{
    [JsonPropertyName("id")]
    public new uint Id { get; set; }

    [JsonPropertyName("gadgetId")]
    public uint GadgetId { get; set; }

    [JsonPropertyName("weaponPromoteId")]
    public uint WeaponPromoteId { get; set; }

    [JsonPropertyName("weaponProp")]
    public List<WeaponPropertyData> WeaponProperties { get; set; } = [];

    [JsonPropertyName("skillAffix")]
    public List<uint> SkillAffix { get; set; } = [];
}

public sealed class WeaponPropertyData
{
    [JsonPropertyName("propType")]
    public string PropType { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string CurveType { get; set; } = string.Empty;

    [JsonPropertyName("initValue")]
    public float InitValue { get; set; }

    public bool TryGetProperty(out FightProperty property) =>
        FightPropertyExtensions.TryParse(PropType, out property) && property != FightProperty.FIGHT_PROP_NONE;
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

    [JsonPropertyName("gadgetId")]
    public uint GadgetId { get; set; }

    [JsonPropertyName("useTarget")]
    public string UseTarget { get; set; } = string.Empty;

    [JsonPropertyName("itemUse")]
    public List<ItemUseData> ItemUse { get; set; } = [];

    [JsonPropertyName("useOnGain")]
    public bool UseOnGain { get; set; }

    public bool IsInventoryMaterial => ItemType == "ITEM_MATERIAL" && !UseOnGain;
}

public sealed class ItemUseData
{
    [JsonPropertyName("useOp")]
    public string UseOp { get; set; } = string.Empty;

    [JsonPropertyName("useParam")]
    public List<string> UseParam { get; set; } = [];
}
