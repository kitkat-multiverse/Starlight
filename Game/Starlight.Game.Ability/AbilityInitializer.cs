using Starlight.Game.Resources;
using Starlight.Game.Resources.Binary;
using System.Globalization;
using System.Text.Json;

namespace Starlight.Game.Ability;

public sealed record AvatarAbilitySources(
    IReadOnlyList<uint> TalentIds,
    uint PromoteLevel,
    uint WeaponAffixId = 0,
    uint WeaponRefinement = 1,
    IReadOnlyList<string>? AbilityGroups = null,
    IReadOnlyList<string>? TalentConfigs = null
);

public sealed class AbilityInitializer(GameData data)
{
    private static readonly HashSet<string> NonHumanoidMoves = [
        "ConfigSimpleMove",
        "ConfigRigidbodyMove",
        "ConfigAnimatorMove",
        "ConfigMixinDriveMove"
    ];

    public AbilityComponent RegisterScene(
        AbilityScope scope,
        uint sceneId,
        AbilityOwner owner,
        IEnumerable<string>? additionalLevelConfigs = null
    )
    {
        var component = scope.RegisterScene(sceneId, owner);
        component.ResetEmbryos(new List<string>());
        component.ClearTargetAbilitySpecials();

        var seeds = new List<AbilityEmbryoSeed>();
        AddNames(seeds, data.GlobalCombat.DefaultAbilities.LevelElementAbilities);
        AddNames(seeds, data.GlobalCombat.DefaultAbilities.LevelDefaultAbilities);
        AddNames(seeds, data.GlobalCombat.DefaultAbilities.LevelItemAbilities);
        AddNames(seeds, data.GlobalCombat.DefaultAbilities.LevelServerBuffAbilities);

        if (data.SceneData.TryGetValue(sceneId, out var scene) && scene.Type == "SCENE_DUNGEON")
            AddNames(seeds, data.GlobalCombat.DefaultAbilities.DungeonAbilities);

        foreach (var level in ResolveLevelEntities(sceneId, additionalLevelConfigs))
        {
            AddAbilities(seeds, level.Abilities);
        }

        InitializeServerAbilities(component, seeds);
        component.ClearServerGlobalValues();
        return component;
    }

    public AbilityComponent RegisterTeam(
        AbilityScope scope,
        AbilityOwner owner,
        uint sceneId,
        IEnumerable<string>? abilityGroups = null,
        IEnumerable<string>? additionalLevelConfigs = null
    )
    {
        var seeds = new List<AbilityEmbryoSeed>();
        AddNames(seeds, data.GlobalCombat.DefaultAbilities.DefaultTeamAbilities);

        foreach (var level in ResolveLevelEntities(sceneId, additionalLevelConfigs))
        {
            AddAbilities(seeds, level.TeamAbilities);
        }

        if (abilityGroups is not null)
        {
            foreach (var groupName in abilityGroups)
            {
                if (data.AbilityGroups.TryGetValue(groupName, out var group))
                    AddAbilities(seeds, group.TargetAbilities);
            }
        }

        var component = scope.Register(owner);
        component.ResetServerAbilities();
        component.ResetEmbryos(seeds);
        component.ClearTargetAbilitySpecials();
        return component;
    }

    public AbilityComponent RegisterMpLevel(AbilityScope scope, AbilityOwner owner)
    {
        var component = scope.Register(owner);
        component.ResetEmbryos(new List<string>());
        component.ClearTargetAbilitySpecials();
        InitializeServerAbilities(component, Seeds(data.GlobalCombat.DefaultAbilities.DefaultMpLevelAbilities));
        component.ClearServerGlobalValues();
        return component;
    }

    public AbilityComponent RegisterAvatar(
        AbilityScope scope,
        AbilityOwner owner,
        uint avatarId,
        uint skillDepotId,
        uint sceneId,
        AvatarAbilitySources? sources = null,
        IEnumerable<AbilityEmbryoSeed>? additional = null,
        IEnumerable<string>? additionalLevelConfigs = null
    )
    {
        var component = scope.Register(owner);
        component.ResetServerAbilities();
        component.ClearTargetAbilitySpecials();

        var seeds = new List<AbilityEmbryoSeed>();

        if (data.Avatars.TryGetValue(avatarId, out var avatar))
            AddAbilities(seeds, avatar.Abilities);

        foreach (var level in ResolveLevelEntities(sceneId, additionalLevelConfigs))
        {
            AddAbilities(seeds, level.AvatarAbilities);
        }

        AddNames(seeds, data.GlobalCombat.DefaultAbilities.DefaultAvatarAbilities);

        data.AvatarSkillDepotData.TryGetValue(skillDepotId, out var depot);

        if (depot is not null &&
            !string.IsNullOrEmpty(depot.SkillDepotAbilityGroup) &&
            data.AbilityGroups.TryGetValue(depot.SkillDepotAbilityGroup, out var skillDepotGroup))
        {
            AddAbilities(seeds, skillDepotGroup.TargetAbilities);
        }

        if (sources is not null)
        {
            foreach (var talentId in sources.TalentIds.Order())
            {
                if (!data.AvatarTalentData.TryGetValue(talentId, out var talent) || string.IsNullOrEmpty(talent.ConfigName))
                    continue;

                ApplyTalent(component, seeds, data.ResolveTalent(talent.ConfigName), []);
            }

            if (depot is not null)
            {
                foreach (var proud in depot.InherentProudSkillOpens
                             .Where(x => x.ProudSkillGroupId != 0 && x.NeedAvatarPromoteLevel <= sources.PromoteLevel)
                             .Select(x => data.ResolveProudSkill(x.ProudSkillGroupId, level: 1))
                             .Where(x => x is not null)
                             .Select(x => x!)
                             .OrderBy(x => x.ProudSkillId))
                {
                    if (!string.IsNullOrEmpty(proud.OpenConfig))
                        ApplyTalent(component, seeds, data.ResolveTalent(proud.OpenConfig), proud.ParamList);
                }
            }

            if (sources.WeaponAffixId != 0)
            {
                var affix = data.ResolveEquipAffix(sources.WeaponAffixId, Math.Max(val1: 1, sources.WeaponRefinement));

                if (affix is not null && !string.IsNullOrEmpty(affix.OpenConfig))
                    ApplyTalent(component, seeds, data.ResolveTalent(affix.OpenConfig), affix.ParamList);
            }

            if (sources.TalentConfigs is not null)
            {
                foreach (var talentName in sources.TalentConfigs)
                {
                    ApplyTalent(component, seeds, data.ResolveTalent(talentName), []);
                }
            }

            if (sources.AbilityGroups is not null)
            {
                foreach (var groupName in sources.AbilityGroups)
                {
                    if (!data.AbilityGroups.TryGetValue(groupName, out var group))
                        continue;

                    AddAbilities(seeds, group.TargetAbilities);

                    foreach (var talent in group.TargetTalents)
                    {
                        if (!string.IsNullOrEmpty(talent.TalentName))
                            ApplyTalent(component, seeds, data.ResolveTalent(talent.TalentName), []);
                    }
                }
            }
        }

        if (additional is not null)
            seeds.AddRange(additional.Where(x => !string.IsNullOrEmpty(x.Name)));

        component.ResetEmbryos(seeds);
        return component;
    }

    public AbilityComponent RegisterWeapon(AbilityScope scope, AbilityOwner owner, uint gadgetId)
    {
        var component = RegisterGadgetCore(scope, owner, gadgetId);
        component.MarkClientInitialized();
        return component;
    }

    public AbilityComponent RegisterGadget(AbilityScope scope, AbilityOwner owner, uint gadgetId) =>
        RegisterGadgetCore(scope, owner, gadgetId);

    public AbilityComponent RegisterClientGadget(AbilityScope scope, AbilityOwner owner, uint gadgetId) =>
        RegisterGadgetCore(scope, owner, gadgetId);

    public AbilityComponent RegisterMonster(
        AbilityScope scope,
        AbilityOwner owner,
        uint monsterId,
        uint sceneId,
        IEnumerable<uint>? groupAffixes = null,
        bool isElite = false,
        bool isLightConfig = false,
        IEnumerable<string>? additionalLevelConfigs = null
    )
    {
        data.MonsterData.TryGetValue(monsterId, out var monster);

        if (monster?.SecurityLevel == "BOSS" && owner.ClientInitInvokeLimit == 0)
            owner = owner with { ClientInitInvokeLimit = 200 };

        var component = scope.Register(owner);
        component.ResetEmbryos(new List<string>());
        component.ClearTargetAbilitySpecials();

        var seeds = new List<AbilityEmbryoSeed>();
        var affixes = new SortedSet<uint>();

        if (groupAffixes is not null)
            affixes.UnionWith(groupAffixes);

        if (monster is not null)
            affixes.UnionWith(monster.Affixes);

        AddAffixAbilities(seeds, affixes, preAdd: true);
        AddNames(seeds, data.GlobalCombat.DefaultAbilities.NonHumanoidMoveAbilities);

        var config = data.ResolveMonsterConfig(monsterId);

        if (config is not null)
            AddAbilities(seeds, config.Abilities, isLightConfig);

        if (isElite && !string.IsNullOrEmpty(data.GlobalCombat.DefaultAbilities.MonsterEliteAbilityName))
            seeds.Add(new AbilityEmbryoSeed(data.GlobalCombat.DefaultAbilities.MonsterEliteAbilityName));

        AddAffixAbilities(seeds, affixes, preAdd: false);

        if (monster?.Type != "MONSTER_PARTNER")
        {
            foreach (var level in ResolveLevelEntities(sceneId, additionalLevelConfigs))
            {
                AddAbilities(seeds, level.MonsterAbilities);
            }
        }

        InitializeServerAbilities(component, seeds);
        ApplyServerGlobals(component, config?.GlobalValue);
        return component;
    }

    public void Append(AbilityComponent component, IEnumerable<AbilityEmbryoSeed> abilities)
    {
        foreach (var ability in abilities)
        {
            if (!string.IsNullOrEmpty(ability.Name))
                component.AddEmbryo(ability.Name, string.IsNullOrEmpty(ability.Override) ? "Default" : ability.Override);
        }
    }

    private AbilityComponent RegisterGadgetCore(AbilityScope scope, AbilityOwner owner, uint gadgetId)
    {
        var component = scope.Register(owner);
        component.ResetEmbryos(new List<string>());
        component.ClearTargetAbilitySpecials();

        var seeds = new List<AbilityEmbryoSeed>();
        var config = data.ResolveGadgetConfig(gadgetId);

        if (config?.Move is { Type.Length: > 0 } move && NonHumanoidMoves.Contains(move.Type))
            AddNames(seeds, data.GlobalCombat.DefaultAbilities.NonHumanoidMoveAbilities);

        if (config is not null)
            AddAbilities(seeds, config.Abilities);

        InitializeServerAbilities(component, seeds);
        ApplyServerGlobals(component, config?.GlobalValue);
        return component;
    }

    private void InitializeServerAbilities(AbilityComponent component, IEnumerable<AbilityEmbryoSeed> abilities)
    {
        component.ResetServerAbilities();

        foreach (var ability in abilities)
        {
            if (string.IsNullOrEmpty(ability.Name))
                continue;

            var definition = data.ResolveAbility(ability.Name);

            if (definition is not null)
                component.AddServerAbility(ability.Name, ability.Override, definition);
        }
    }

    private void AddAffixAbilities(List<AbilityEmbryoSeed> seeds, IEnumerable<uint> affixes, bool preAdd)
    {
        foreach (var affixId in affixes)
        {
            if (!data.MonsterAffixData.TryGetValue(affixId, out var affix) || affix.IsPreAdd != preAdd)
                continue;

            AddNames(seeds, affix.AbilityNames);
        }
    }

    private IEnumerable<ConfigLevelEntity> ResolveLevelEntities(uint sceneId, IEnumerable<string>? additional)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);

        if (data.SceneData.TryGetValue(sceneId, out var scene) &&
            !string.IsNullOrEmpty(scene.LevelEntityConfig) &&
            names.Add(scene.LevelEntityConfig) &&
            data.LevelEntityConfigs.TryGetValue(scene.LevelEntityConfig, out var primary))
        {
            yield return primary;
        }

        if (additional is null)
            yield break;

        foreach (var name in additional)
        {
            if (!string.IsNullOrEmpty(name) && names.Add(name) && data.LevelEntityConfigs.TryGetValue(name, out var level))
                yield return level;
        }
    }

    private void ApplyTalent(
        AbilityComponent component,
        List<AbilityEmbryoSeed> seeds,
        IReadOnlyList<TalentConfigEntry> entries,
        IReadOnlyList<float> parameters
    )
    {
        foreach (var entry in entries)
        {
            if (entry.Type == "AddAbility")
            {
                if (!string.IsNullOrEmpty(entry.AbilityName))
                    seeds.Add(new AbilityEmbryoSeed(entry.AbilityName));
                continue;
            }

            if (entry.Type != "ModifyAbility" || string.IsNullOrEmpty(entry.AbilityName) || string.IsNullOrEmpty(entry.ParamSpecial))
                continue;

            var delta = EvaluateTalentValue(entry.ParamDelta, parameters, fallback: 0);
            var ratio = EvaluateTalentValue(entry.ParamRatio, parameters, fallback: 0);

            component.AddTargetAbilitySpecial(
                AbilityKey.FromName(entry.AbilityName),
                AbilityKey.FromName(entry.ParamSpecial),
                delta,
                ratio);
        }
    }

    private static float EvaluateTalentValue(JsonElement value, IReadOnlyList<float> parameters, float fallback)
    {
        if (value.ValueKind == JsonValueKind.Number && value.TryGetSingle(out var number))
            return number;

        if (value.ValueKind != JsonValueKind.String)
            return fallback;

        var expression = value.GetString();

        if (string.IsNullOrEmpty(expression))
            return fallback;

        if (float.TryParse(expression, NumberStyles.Float, CultureInfo.InvariantCulture, out var literal))
            return literal;

        var negative = expression.StartsWith("-%", StringComparison.Ordinal);
        var offset = negative ? 2 : expression.StartsWith('%') ? 1 : 0;

        if (offset == 0 || !int.TryParse(expression.AsSpan(offset), out var index) || index <= 0 || index > parameters.Count)
            return fallback;

        var result = parameters[index - 1];
        return negative ? -result : result;
    }

    private static void AddNames(List<AbilityEmbryoSeed> seeds, IEnumerable<string> names)
    {
        foreach (var name in names)
        {
            if (!string.IsNullOrEmpty(name))
                seeds.Add(new AbilityEmbryoSeed(name));
        }
    }

    private static void AddAbilities(List<AbilityEmbryoSeed> seeds, IEnumerable<ConfigAbilityData> abilities, bool lightOnly = false)
    {
        foreach (var ability in abilities)
        {
            if (!string.IsNullOrEmpty(ability.AbilityName) && (!lightOnly || !ability.LightWeightRemove))
                seeds.Add(new AbilityEmbryoSeed(ability.AbilityName, ability.AbilityOverride));
        }
    }

    private static IEnumerable<AbilityEmbryoSeed> Seeds(IEnumerable<string> names)
    {
        foreach (var name in names)
        {
            if (!string.IsNullOrEmpty(name))
                yield return new AbilityEmbryoSeed(name);
        }
    }

    private static void ApplyServerGlobals(AbilityComponent component, ConfigEntityGlobalValue? values)
    {
        component.ClearServerGlobalValues();

        if (values is null)
            return;

        foreach (var name in values.ServerGlobalValues)
        {
            component.SetServerGlobalValue(AbilityKey.FromName(name), AbilityScalarValue.FromFloat(0));
        }

        foreach (var (name, value) in values.InitServerGlobalValues)
        {
            component.SetServerGlobalValue(AbilityKey.FromName(name), AbilityScalarValue.FromFloat(value));
        }
    }
}
