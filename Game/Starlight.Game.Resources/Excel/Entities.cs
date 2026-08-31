using System.Text.Json.Serialization;

namespace Starlight.Game.Resources.Excel;

[GameResource("GadgetExcelConfigData.json")]
public sealed class GadgetData : Data
{
    [JsonPropertyName("id")]
    public new uint Id { get; set; }

    [JsonPropertyName("jsonName")]
    public string JsonName { get; set; } = string.Empty;

    [JsonPropertyName("itemJsonName")]
    public string ItemJsonName { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;
}

[GameResource("MonsterExcelConfigData.json")]
public sealed class MonsterData : Data
{
    [JsonPropertyName("id")]
    public new uint Id { get; set; }

    [JsonPropertyName("monsterName")]
    public string MonsterName { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("affix")]
    public List<uint> Affixes { get; set; } = [];

    [JsonPropertyName("equips")]
    public List<uint> Equips { get; set; } = [];

    [JsonPropertyName("securityLevel")]
    public string SecurityLevel { get; set; } = string.Empty;
}

[GameResource("MonsterAffixExcelConfigData.json")]
public sealed class MonsterAffixData : Data
{
    [JsonPropertyName("id")]
    public new uint Id { get; set; }

    [JsonPropertyName("isPreAdd")]
    public bool IsPreAdd { get; set; }

    [JsonPropertyName("abilityName")]
    public List<string> AbilityNames { get; set; } = [];
}

[GameResource("SceneExcelConfigData.json")]
public sealed class SceneData : Data
{
    [JsonPropertyName("id")]
    public new uint Id { get; set; }

    [JsonPropertyName("levelEntityConfig")]
    public string LevelEntityConfig { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;
}
