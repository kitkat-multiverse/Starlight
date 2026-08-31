using System.Text.Json;
using System.Text.Json.Serialization;

namespace Starlight.Game.Resources.Binary;

public sealed class TalentConfigEntry
{
    [JsonPropertyName("$type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("abilityName")]
    public string AbilityName { get; set; } = string.Empty;

    [JsonPropertyName("paramSpecial")]
    public string ParamSpecial { get; set; } = string.Empty;

    [JsonPropertyName("paramDelta")]
    public JsonElement ParamDelta { get; set; }

    [JsonPropertyName("paramRatio")]
    public JsonElement ParamRatio { get; set; }
}

public sealed class ProudSkillResourceData
{
    [JsonPropertyName("proudSkillId")]
    public uint ProudSkillId { get; set; }

    [JsonPropertyName("proudSkillGroupId")]
    public uint ProudSkillGroupId { get; set; }

    [JsonPropertyName("level")]
    public uint Level { get; set; }

    [JsonPropertyName("breakLevel")]
    public uint BreakLevel { get; set; }

    [JsonPropertyName("openConfig")]
    public string OpenConfig { get; set; } = string.Empty;

    [JsonPropertyName("paramList")]
    public List<float> ParamList { get; set; } = [];
}

public sealed class EquipAffixResourceData
{
    [JsonPropertyName("id")]
    public uint Id { get; set; }

    [JsonPropertyName("affixId")]
    public uint AffixId { get; set; }

    [JsonPropertyName("openConfig")]
    public string OpenConfig { get; set; } = string.Empty;

    [JsonPropertyName("paramList")]
    public List<float> ParamList { get; set; } = [];
}
