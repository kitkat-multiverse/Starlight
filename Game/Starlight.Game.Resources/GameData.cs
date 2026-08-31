using JetBrains.Annotations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Starlight.Game.Resources.Binary;
using Starlight.Game.Resources.Excel;

namespace Starlight.Game.Resources;

public sealed class GameData(IConfiguration config) : IHostedService
{
    #region Excel

    [UsedImplicitly] public readonly Dictionary<uint, AvatarData> AvatarData = new();
    [UsedImplicitly] public readonly Dictionary<uint, AvatarSkillDepotData> AvatarSkillDepotData = new();
    [UsedImplicitly] public readonly Dictionary<uint, AvatarTalentData> AvatarTalentData = new();
    [UsedImplicitly] public readonly Dictionary<uint, WeaponData> WeaponData = new();
    [UsedImplicitly] public readonly Dictionary<uint, MaterialData> MaterialData = new();
    [UsedImplicitly] public readonly Dictionary<uint, CoopPointData> CoopPointData = new();
    [UsedImplicitly] public readonly Dictionary<uint, GadgetData> GadgetData = new();
    [UsedImplicitly] public readonly Dictionary<uint, MonsterData> MonsterData = new();
    [UsedImplicitly] public readonly Dictionary<uint, MonsterAffixData> MonsterAffixData = new();
    [UsedImplicitly] public readonly Dictionary<uint, SceneData> SceneData = new();

    #endregion

    #region Binary

    public readonly Dictionary<uint, AvatarConfig> Avatars = new();
    public readonly Dictionary<uint, Dictionary<uint, PointData>> ScenePoints = new();
    public readonly Dictionary<string, AbilityConfig> Abilities = new(StringComparer.Ordinal);
    public readonly Dictionary<uint, List<AbilityConfig>> AbilitiesByHash = new();
    public readonly Dictionary<string, AbilityGroupConfig> AbilityGroups = new(StringComparer.Ordinal);
    public readonly Dictionary<string, IReadOnlyList<string>> AbilityPaths = new(StringComparer.Ordinal);
    public readonly Dictionary<string, IReadOnlyList<string>> GadgetAbilityPaths = new(StringComparer.Ordinal);
    public readonly Dictionary<string, ConfigEntityGadget> GadgetConfigs = new(StringComparer.Ordinal);
    public readonly Dictionary<string, ConfigEntityMonster> MonsterConfigs = new(StringComparer.Ordinal);
    public readonly Dictionary<string, ConfigLevelEntity> LevelEntityConfigs = new(StringComparer.Ordinal);
    public readonly Dictionary<string, IReadOnlyList<TalentConfigEntry>> Talents = new(StringComparer.Ordinal);
    public readonly Dictionary<uint, ProudSkillResourceData> ProudSkills = new();
    public readonly Dictionary<(uint GroupId, uint Level), ProudSkillResourceData> ProudSkillsByGroupAndLevel = new();
    public readonly Dictionary<(uint GroupId, uint Level), EquipAffixResourceData> EquipAffixesByGroupAndLevel = new();
    public readonly HashSet<uint> ServerGlobalValueHashes = [];
    public ConfigGlobalCombat GlobalCombat { get; internal set; } = new();

    #endregion

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var path = config.GetValue<string>("Game:ResourcesPath") ?? "./resources.zip";

        Resources.Initialize(path);
        DataLoader.Initialize(this);

        return Task.CompletedTask;
    }

    public AbilityConfig? ResolveAbility(string name) =>
        Abilities.GetValueOrDefault(name);

    public AbilityConfig? ResolveAbility(uint hash) =>
        AbilitiesByHash.TryGetValue(hash, out var entries) && entries.Count == 1 ? entries[0] : null;

    public ConfigEntityGadget? ResolveGadgetConfig(uint gadgetId)
    {
        if (!GadgetData.TryGetValue(gadgetId, out var gadget) || string.IsNullOrEmpty(gadget.JsonName))
            return null;

        return GadgetConfigs.GetValueOrDefault(gadget.JsonName);
    }

    public ConfigEntityMonster? ResolveMonsterConfig(uint monsterId)
    {
        if (!MonsterData.TryGetValue(monsterId, out var monster) || string.IsNullOrEmpty(monster.MonsterName))
            return null;

        return MonsterConfigs.GetValueOrDefault($"{monster.MonsterName}_{monsterId}")
               ?? MonsterConfigs.GetValueOrDefault(monster.MonsterName);
    }

    public ConfigLevelEntity? ResolveLevelEntity(uint sceneId)
    {
        if (!SceneData.TryGetValue(sceneId, out var scene) || string.IsNullOrEmpty(scene.LevelEntityConfig))
            return null;

        return LevelEntityConfigs.GetValueOrDefault(scene.LevelEntityConfig);
    }

    public IReadOnlyList<TalentConfigEntry> ResolveTalent(string name) =>
        Talents.GetValueOrDefault(name) ?? [];

    public ProudSkillResourceData? ResolveProudSkill(uint groupId, uint level = 1) =>
        ProudSkillsByGroupAndLevel.GetValueOrDefault((groupId, level));

    public EquipAffixResourceData? ResolveEquipAffix(uint groupId, uint refinement) =>
        EquipAffixesByGroupAndLevel.GetValueOrDefault((groupId, refinement));

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
