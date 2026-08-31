using System.Text.Json.Serialization;

namespace Starlight.Game.Resources.Excel;

[GameResource("AvatarExcelConfigData.json")]
public sealed class AvatarData : Data
{
    [JsonPropertyName("id")]
    public new uint Id { get; set; }

    [JsonPropertyName("iconName")]
    public string IconName { get; set; } = string.Empty;

    [JsonPropertyName("initialWeapon")]
    public uint InitialWeapon { get; set; }

    [JsonPropertyName("skillDepotId")]
    public uint SkillDepotId { get; set; }

    [JsonPropertyName("hpBase")]
    public float HpBase { get; set; }

    [JsonPropertyName("attackBase")]
    public float AttackBase { get; set; }

    [JsonPropertyName("defenseBase")]
    public float DefenseBase { get; set; }

    [JsonPropertyName("critical")]
    public float CritChanceBase { get; set; }

    [JsonPropertyName("criticalHurt")]
    public float CritDamageBase { get; set; }

    public string AvatarName => IconName.Split('_').Last();
}

[GameResource("AvatarSkillDepotExcelConfigData.json")]
public sealed class AvatarSkillDepotData : Data
{
    [JsonPropertyName("id")]
    public new uint Id { get; set; }

    [JsonPropertyName("skills")]
    public List<uint> Skills { get; set; } = [];

    [JsonPropertyName("energySkill")]
    public uint EnergySkill { get; set; }

    [JsonPropertyName("talents")]
    public List<uint> Talents { get; set; } = [];

    [JsonPropertyName("talentStarName")]
    public string TalentStarName { get; set; } = string.Empty;

    [JsonPropertyName("skillDepotAbilityGroup")]
    public string SkillDepotAbilityGroup { get; set; } = string.Empty;

    [JsonPropertyName("extraAbilities")]
    public List<string> ExtraAbilities { get; set; } = [];

    [JsonPropertyName("inherentProudSkillOpens")]
    public List<InherentProudSkillOpenData> InherentProudSkillOpens { get; set; } = [];
}

[GameResource("AvatarTalentExcelConfigData.json")]
public sealed class AvatarTalentData : Data
{
    [JsonPropertyName("talentId")]
    public new uint Id { get; set; }

    [JsonPropertyName("openConfig")]
    public string ConfigName { get; set; } = string.Empty;
}

public sealed class InherentProudSkillOpenData
{
    [JsonPropertyName("proudSkillGroupId")]
    public uint ProudSkillGroupId { get; set; }

    [JsonPropertyName("needAvatarPromoteLevel")]
    public uint NeedAvatarPromoteLevel { get; set; }
}
