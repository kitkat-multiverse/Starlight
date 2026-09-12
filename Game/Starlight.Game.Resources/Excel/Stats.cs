using System.Text.Json.Serialization;

namespace Starlight.Game.Resources.Excel;

public sealed class FightPropData
{
    [JsonPropertyName("propType")]
    public string PropType { get; set; } = string.Empty;

    [JsonPropertyName("value")]
    public float Value { get; set; }

    public bool TryGetProperty(out FightProperty property) =>
        FightPropertyExtensions.TryParse(PropType, out property) && property != FightProperty.FIGHT_PROP_NONE;
}

public sealed class PropGrowCurveData
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("growCurve")]
    public string GrowCurve { get; set; } = string.Empty;
}

public sealed class CurveInfoData
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("arith")]
    public string Arithmetic { get; set; } = string.Empty;

    [JsonPropertyName("value")]
    public float Value { get; set; } = 1f;
}

[GameResource("AvatarCurveExcelConfigData.json")]
public sealed class AvatarCurveData : Data
{
    private Dictionary<string, float> _curveInfoMap = new(StringComparer.Ordinal);
    [JsonPropertyName("level")]
    public uint Level { get; set; }

    [JsonPropertyName("curveInfos")]
    public List<CurveInfoData> CurveInfos { get; set; } = [];

    public override void OnLoad()
    {
        Id = Level;

        _curveInfoMap = CurveInfos
            .Where(info => !string.IsNullOrEmpty(info.Type))
            .GroupBy(info => info.Type, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Last().Value, StringComparer.Ordinal);
    }

    public float GetMultiplier(string curve) =>
        string.IsNullOrEmpty(curve) ? 1f : _curveInfoMap.GetValueOrDefault(curve, defaultValue: 1f);
}

[GameResource("MonsterCurveExcelConfigData.json")]
public sealed class MonsterCurveData : Data
{
    private Dictionary<string, float> _curveInfoMap = new(StringComparer.Ordinal);
    [JsonPropertyName("level")]
    public uint Level { get; set; }

    [JsonPropertyName("curveInfos")]
    public List<CurveInfoData> CurveInfos { get; set; } = [];

    public override void OnLoad()
    {
        Id = Level;

        _curveInfoMap = CurveInfos
            .Where(info => !string.IsNullOrEmpty(info.Type))
            .GroupBy(info => info.Type, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Last().Value, StringComparer.Ordinal);
    }

    public float GetMultiplier(string curve) =>
        string.IsNullOrEmpty(curve) ? 1f : _curveInfoMap.GetValueOrDefault(curve, defaultValue: 1f);
}

[GameResource("AvatarPromoteExcelConfigData.json")]
public sealed class AvatarPromoteData : Data
{
    [JsonPropertyName("avatarPromoteId")]
    public uint AvatarPromoteId { get; set; }

    [JsonPropertyName("promoteLevel")]
    public uint PromoteLevel { get; set; }

    [JsonPropertyName("unlockMaxLevel")]
    public uint UnlockMaxLevel { get; set; }

    [JsonPropertyName("addProps")]
    public List<FightPropData> AddProps { get; set; } = [];

    public override void OnLoad() => Id = AvatarPromoteId << 8 | PromoteLevel;
}

[GameResource("WeaponCurveExcelConfigData.json")]
public sealed class WeaponCurveData : Data
{
    private Dictionary<string, float> _curveInfoMap = new(StringComparer.Ordinal);
    [JsonPropertyName("level")]
    public uint Level { get; set; }

    [JsonPropertyName("curveInfos")]
    public List<CurveInfoData> CurveInfos { get; set; } = [];

    public override void OnLoad()
    {
        Id = Level;

        _curveInfoMap = CurveInfos
            .Where(info => !string.IsNullOrEmpty(info.Type))
            .GroupBy(info => info.Type, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Last().Value, StringComparer.Ordinal);
    }

    public float GetMultiplier(string curve) =>
        string.IsNullOrEmpty(curve) ? 1f : _curveInfoMap.GetValueOrDefault(curve, defaultValue: 1f);
}

[GameResource("WeaponPromoteExcelConfigData.json")]
public sealed class WeaponPromoteData : Data
{
    [JsonPropertyName("weaponPromoteId")]
    public uint WeaponPromoteId { get; set; }

    [JsonPropertyName("promoteLevel")]
    public uint PromoteLevel { get; set; }

    [JsonPropertyName("unlockMaxLevel")]
    public uint UnlockMaxLevel { get; set; }

    [JsonPropertyName("addProps")]
    public List<FightPropData> AddProps { get; set; } = [];

    public override void OnLoad() => Id = WeaponPromoteId << 8 | PromoteLevel;
}
