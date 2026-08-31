using System.Text.Json;
using System.Text.Json.Serialization;

namespace Starlight.Game.Resources.Binary;

public sealed class ConfigAbilityData
{
    [JsonPropertyName("abilityID")]
    public string AbilityId { get; set; } = string.Empty;

    [JsonPropertyName("abilityName")]
    public string AbilityName { get; set; } = string.Empty;

    [JsonPropertyName("abilityOverride")]
    public string AbilityOverride { get; set; } = string.Empty;

    [JsonPropertyName("lightWeightRemove")]
    public bool LightWeightRemove { get; set; }
}

public sealed class AbilityConfigEntry
{
    [JsonPropertyName("Default")]
    public AbilityConfig? Default { get; set; }
}

public sealed class AbilityConfig
{
    [JsonPropertyName("abilityName")]
    public string AbilityName { get; set; } = string.Empty;

    [JsonPropertyName("isDynamicAbility")]
    public bool IsDynamicAbility { get; set; }

    [JsonPropertyName("abilitySpecials")]
    public JsonElement AbilitySpecials { get; set; }

    [JsonPropertyName("modifiers")]
    public Dictionary<string, AbilityModifierConfig> Modifiers { get; set; } = [];

    [JsonPropertyName("abilityMixins")]
    public List<AbilityConfigNode> AbilityMixins { get; set; } = [];

    [JsonPropertyName("onAdded")]
    public List<AbilityConfigNode> OnAdded { get; set; } = [];

    [JsonPropertyName("onRemoved")]
    public List<AbilityConfigNode> OnRemoved { get; set; } = [];

    [JsonPropertyName("onAbilityStart")]
    public List<AbilityConfigNode> OnAbilityStart { get; set; } = [];

    [JsonPropertyName("onKill")]
    public List<AbilityConfigNode> OnKill { get; set; } = [];

    [JsonPropertyName("onFieldEnter")]
    public List<AbilityConfigNode> OnFieldEnter { get; set; } = [];

    [JsonPropertyName("onFieldExit")]
    public List<AbilityConfigNode> OnFieldExit { get; set; } = [];

    [JsonPropertyName("onExit")]
    public List<AbilityConfigNode>? OnExit
    {
        set
        {
            if (value is not null && OnFieldExit.Count == 0)
                OnFieldExit = value;
        }
    }

    [JsonPropertyName("onAttach")]
    public List<AbilityConfigNode> OnAttach { get; set; } = [];

    [JsonPropertyName("onDetach")]
    public List<AbilityConfigNode> OnDetach { get; set; } = [];

    [JsonPropertyName("onAvatarIn")]
    public List<AbilityConfigNode> OnAvatarIn { get; set; } = [];

    [JsonPropertyName("onAvatarOut")]
    public List<AbilityConfigNode> OnAvatarOut { get; set; } = [];

    [JsonPropertyName("onVehicleIn")]
    public List<AbilityConfigNode> OnVehicleIn { get; set; } = [];

    [JsonPropertyName("onVehicleOut")]
    public List<AbilityConfigNode> OnVehicleOut { get; set; } = [];

    [JsonPropertyName("onTriggerAvatarRay")]
    public List<AbilityConfigNode> OnTriggerAvatarRay { get; set; } = [];

    [JsonPropertyName("onZoneEnter")]
    public List<AbilityConfigNode> OnZoneEnter { get; set; } = [];

    [JsonPropertyName("onZoneExit")]
    public List<AbilityConfigNode> OnZoneExit { get; set; } = [];

    [JsonExtensionData]
    public Dictionary<string, JsonElement> Extra { get; set; } = [];

    [JsonIgnore]
    public uint NameHash { get; internal set; }

    [JsonIgnore]
    public IReadOnlyList<string> ModifierNames { get; internal set; } = [];

    [JsonIgnore]
    public IReadOnlyDictionary<int, AbilityConfigNode> ActionsByLocalId { get; internal set; } =
        new Dictionary<int, AbilityConfigNode>();

    [JsonIgnore]
    public IReadOnlyDictionary<int, AbilityConfigNode> MixinsByLocalId { get; internal set; } =
        new Dictionary<int, AbilityConfigNode>();

    public string? ModifierName(int localId) =>
        localId >= 0 && localId < ModifierNames.Count ? ModifierNames[localId] : null;

    public AbilityConfigNode? ResolveAction(int localId) => ActionsByLocalId.GetValueOrDefault(localId);

    public AbilityConfigNode? ResolveMixin(int localId) => MixinsByLocalId.GetValueOrDefault(localId);

    internal void Initialize() => AbilityLocalIdIndex.Initialize(this);
}

public sealed class AbilityModifierConfig
{
    [JsonPropertyName("modifierMixins")]
    public List<AbilityConfigNode> ModifierMixins { get; set; } = [];

    [JsonPropertyName("onAdded")]
    public List<AbilityConfigNode> OnAdded { get; set; } = [];

    [JsonPropertyName("onRemoved")]
    public List<AbilityConfigNode> OnRemoved { get; set; } = [];

    [JsonPropertyName("onBeingHit")]
    public List<AbilityConfigNode> OnBeingHit { get; set; } = [];

    [JsonPropertyName("onAttackLanded")]
    public List<AbilityConfigNode> OnAttackLanded { get; set; } = [];

    [JsonPropertyName("onHittingOther")]
    public List<AbilityConfigNode> OnHittingOther { get; set; } = [];

    [JsonPropertyName("onThinkInterval")]
    public List<AbilityConfigNode> OnThinkInterval { get; set; } = [];

    [JsonPropertyName("onKill")]
    public List<AbilityConfigNode> OnKill { get; set; } = [];

    [JsonPropertyName("onCrash")]
    public List<AbilityConfigNode> OnCrash { get; set; } = [];

    [JsonPropertyName("onAvatarIn")]
    public List<AbilityConfigNode> OnAvatarIn { get; set; } = [];

    [JsonPropertyName("onAvatarOut")]
    public List<AbilityConfigNode> OnAvatarOut { get; set; } = [];

    [JsonPropertyName("onReconnect")]
    public List<AbilityConfigNode> OnReconnect { get; set; } = [];

    [JsonPropertyName("onChangeAuthority")]
    public List<AbilityConfigNode> OnChangeAuthority { get; set; } = [];

    [JsonPropertyName("onVehicleIn")]
    public List<AbilityConfigNode> OnVehicleIn { get; set; } = [];

    [JsonPropertyName("onVehicleOut")]
    public List<AbilityConfigNode> OnVehicleOut { get; set; } = [];

    [JsonPropertyName("onZoneEnter")]
    public List<AbilityConfigNode> OnZoneEnter { get; set; } = [];

    [JsonPropertyName("onZoneExit")]
    public List<AbilityConfigNode> OnZoneExit { get; set; } = [];

    [JsonPropertyName("onHeal")]
    public List<AbilityConfigNode> OnHeal { get; set; } = [];

    [JsonPropertyName("onBeingHealed")]
    public List<AbilityConfigNode> OnBeingHealed { get; set; } = [];

    [JsonPropertyName("properties")]
    public Dictionary<string, JsonElement> Properties { get; set; } = [];

    [JsonPropertyName("elementDurability")]
    public JsonElement ElementDurability { get; set; }

    [JsonPropertyName("isUnique")]
    public bool IsUnique { get; set; }

    [JsonPropertyName("isLimitedProperties")]
    public bool IsLimitedProperties { get; set; }

    [JsonPropertyName("reApplyModifierOnStateChange")]
    public bool ReapplyModifierOnStateChange { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement> Extra { get; set; } = [];
}

public sealed class AbilityConfigNode
{
    [JsonPropertyName("$type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("actions")]
    public List<AbilityConfigNode> Actions { get; set; } = [];

    [JsonPropertyName("successActions")]
    public List<AbilityConfigNode> SuccessActions { get; set; } = [];

    [JsonPropertyName("failActions")]
    public List<AbilityConfigNode> FailActions { get; set; } = [];

    [JsonPropertyName("succActions")]
    public List<AbilityConfigNode> SuccActions { get; set; } = [];

    [JsonPropertyName("onStageReady")]
    public List<AbilityConfigNode> OnStageReady { get; set; } = [];

    [JsonPropertyName("onEatFood")]
    public List<AbilityConfigNode> OnEatFood { get; set; } = [];

    [JsonPropertyName("onEnterArea")]
    public List<AbilityConfigNode> OnEnterArea { get; set; } = [];

    [JsonPropertyName("onExitArea")]
    public List<AbilityConfigNode> OnExitArea { get; set; } = [];

    [JsonPropertyName("onSelectStart")]
    public List<AbilityConfigNode> OnSelectStart { get; set; } = [];

    [JsonPropertyName("onSelectEnd")]
    public List<AbilityConfigNode> OnSelectEnd { get; set; } = [];

    [JsonPropertyName("onBeingHit")]
    public List<AbilityConfigNode> OnBeingHit { get; set; } = [];

    [JsonPropertyName("onAttackLanded")]
    public List<AbilityConfigNode> OnAttackLanded { get; set; } = [];

    [JsonPropertyName("onHittingOther")]
    public List<AbilityConfigNode> OnHittingOther { get; set; } = [];

    [JsonPropertyName("onThinkInterval")]
    public List<AbilityConfigNode> OnThinkInterval { get; set; } = [];

    [JsonPropertyName("onKill")]
    public List<AbilityConfigNode> OnKill { get; set; } = [];

    [JsonPropertyName("onCrash")]
    public List<AbilityConfigNode> OnCrash { get; set; } = [];

    [JsonPropertyName("onAvatarIn")]
    public List<AbilityConfigNode> OnAvatarIn { get; set; } = [];

    [JsonPropertyName("onAvatarOut")]
    public List<AbilityConfigNode> OnAvatarOut { get; set; } = [];

    [JsonPropertyName("onReconnect")]
    public List<AbilityConfigNode> OnReconnect { get; set; } = [];

    [JsonPropertyName("onChangeAuthority")]
    public List<AbilityConfigNode> OnChangeAuthority { get; set; } = [];

    [JsonPropertyName("onVehicleIn")]
    public List<AbilityConfigNode> OnVehicleIn { get; set; } = [];

    [JsonPropertyName("onVehicleOut")]
    public List<AbilityConfigNode> OnVehicleOut { get; set; } = [];

    [JsonPropertyName("onZoneEnter")]
    public List<AbilityConfigNode> OnZoneEnter { get; set; } = [];

    [JsonPropertyName("onZoneExit")]
    public List<AbilityConfigNode> OnZoneExit { get; set; } = [];

    [JsonPropertyName("onHeal")]
    public List<AbilityConfigNode> OnHeal { get; set; } = [];

    [JsonPropertyName("onBeingHealed")]
    public List<AbilityConfigNode> OnBeingHealed { get; set; } = [];

    [JsonExtensionData]
    public Dictionary<string, JsonElement> Values { get; set; } = [];
}

public sealed class ConfigGlobalCombat
{
    [JsonPropertyName("defaultAbilities")]
    public DefaultAbilityConfig DefaultAbilities { get; set; } = new();
}

public sealed class DefaultAbilityConfig
{
    [JsonPropertyName("levelElementAbilities")]
    public List<string> LevelElementAbilities { get; set; } = [];

    [JsonPropertyName("levelDefaultAbilities")]
    public List<string> LevelDefaultAbilities { get; set; } = [];

    [JsonPropertyName("levelItemAbilities")]
    public List<string> LevelItemAbilities { get; set; } = [];

    [JsonPropertyName("levelSBuffAbilities")]
    public List<string> LevelServerBuffAbilities { get; set; } = [];

    [JsonPropertyName("dungeonAbilities")]
    public List<string> DungeonAbilities { get; set; } = [];

    [JsonPropertyName("evaluateGlobalValueAbilities")]
    public List<string> EvaluateGlobalValueAbilities { get; set; } = [];

    [JsonPropertyName("defaultAvatarAbilities")]
    public List<string> DefaultAvatarAbilities { get; set; } = [];

    [JsonPropertyName("defaultTeamAbilities")]
    public List<string> DefaultTeamAbilities { get; set; } = [];

    [JsonPropertyName("defaultMPLevelAbilities")]
    public List<string> DefaultMpLevelAbilities { get; set; } = [];

    [JsonPropertyName("nonHumanoidMoveAbilities")]
    public List<string> NonHumanoidMoveAbilities { get; set; } = [];

    [JsonPropertyName("monterEliteAbilityName")]
    public string MonsterEliteAbilityName { get; set; } = string.Empty;
}

public sealed class AbilityGroupConfig
{
    [JsonPropertyName("abilityGroupSourceType")]
    public string SourceType { get; set; } = string.Empty;

    [JsonPropertyName("abilityGroupTargetType")]
    public string TargetType { get; set; } = string.Empty;

    [JsonPropertyName("targetAbilities")]
    public List<ConfigAbilityData> TargetAbilities { get; set; } = [];

    [JsonPropertyName("targetTalents")]
    public List<ConfigTalentData> TargetTalents { get; set; } = [];
}

public sealed class ConfigTalentData
{
    [JsonPropertyName("talentName")]
    public string TalentName { get; set; } = string.Empty;
}

public sealed class AbilityPathConfig
{
    [JsonPropertyName("abilityPaths")]
    public Dictionary<string, List<string>> AbilityPaths { get; set; } = [];
}
