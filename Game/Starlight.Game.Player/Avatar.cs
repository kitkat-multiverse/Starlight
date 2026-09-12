using Starlight.Game.Resources;
using Starlight.Game.Resources.Excel;
using Starlight.Protocol;

namespace Starlight.Game.Player;

public sealed class Avatar
{
    public const uint DefaultFlycloak = 140001;

    private const uint Alive = 1;
    private const uint Dead = 2;
    private const uint AvatarTypeFormal = 1;

    private readonly GameData _data;
    private readonly Dictionary<uint, uint> _skillLevelMap;
    private readonly HashSet<uint> _talentIds;

    private Avatar(GameData data, IEnumerable<uint> talentIds, IReadOnlyDictionary<uint, uint>? skillLevels)
    {
        _data = data;
        _talentIds = [.. talentIds.Where(id => id != 0)];

        _skillLevelMap = skillLevels is null ?
            [] :
            skillLevels
                .Where(pair => pair.Key != 0 && pair.Value != 0)
                .ToDictionary(pair => pair.Key, pair => pair.Value);
    }

    public uint AvatarId { get; private init; }
    public ulong Guid { get; private init; }
    public uint BornTime { get; private init; }

    public uint SkillDepotId { get; private set; }
    public IReadOnlyList<uint> Skills => [.. SkillDepot.SkillsAndEnergySkill];
    public IReadOnlyList<uint> Talents => [.. SkillDepot.Talents.Where(_talentIds.Contains)];
    public IReadOnlyList<uint> CandSkillDepotIds => AvatarConfig.CandSkillDepotIds;

    public uint Level { get; private init; } = 1;
    public uint PromoteLevel { get; private init; }
    public uint Constellation => (uint)Talents.Count;
    public uint CoreProudSkillLevel => GetCoreProudSkillLevel();

    public uint WeaponItemId { get; private set; }
    public uint WeaponGadgetId { get; private set; }
    public ulong WeaponGuid { get; private set; }
    public WeaponItem? Weapon { get; private set; }

    public IReadOnlyDictionary<uint, float> FightProps => FightPropertyStore;
    public FightPropertyStore FightPropertyStore { get; } = new();

    public IReadOnlyDictionary<uint, uint> AllSkillLevels => _skillLevelMap;
    public IReadOnlyCollection<uint> AllTalentIds => _talentIds;

    private AvatarData AvatarConfig => _data.AvatarData[AvatarId];
    private AvatarSkillDepotData SkillDepot => _data.AvatarSkillDepotData[SkillDepotId];

    public static Avatar Create(
        GameData data,
        uint avatarId,
        ulong guid,
        uint level = 1,
        uint constellation = 0,
        uint bornTime = 0,
        ulong weaponGuid = 0,
        uint skillDepotId = 0,
        IEnumerable<uint>? talentIds = null,
        IReadOnlyDictionary<uint, uint>? skillLevels = null
    )
    {
        var config = data.AvatarData[avatarId];
        var resolvedDepotId = ResolveSkillDepotId(data, config, skillDepotId);
        var depot = data.AvatarSkillDepotData[resolvedDepotId];
        var weapon = data.WeaponData[config.InitialWeapon];

        level = Math.Clamp(level, min: 1u, max: 90u);
        constellation = Math.Clamp(constellation, min: 0u, max: 6u);

        var unlockedTalents = talentIds?.Where(id => id != 0).ToArray();

        if (unlockedTalents is not { Length: > 0 } && constellation != 0)
            unlockedTalents = [.. depot.Talents.Where(id => id != 0).Take((int)constellation)];

        var avatar = new Avatar(data, unlockedTalents ?? [], skillLevels) {
            AvatarId = avatarId,
            Guid = guid,
            SkillDepotId = resolvedDepotId,
            BornTime = bornTime == 0 ? (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds() : bornTime,
            WeaponItemId = config.InitialWeapon,
            WeaponGadgetId = weapon.GadgetId,
            WeaponGuid = weaponGuid == 0 ? guid + 1 : weaponGuid,
            Level = level,
            PromoteLevel = PromoteLevelFor(level)
        };

        avatar.EnsureCurrentDepotSkills();
        avatar.RecalculateFightProperties();
        return avatar;
    }

    internal void EquipWeapon(WeaponItem weapon, bool recalculate = true)
    {
        WeaponItemId = weapon.ItemId;
        WeaponGadgetId = weapon.GadgetId;
        WeaponGuid = weapon.Guid;
        Weapon = weapon;
        weapon.EquipAvatarId = AvatarId;

        if (recalculate)
            RecalculateFightProperties();
    }

    internal WeaponItem? UnequipWeapon()
    {
        var weapon = Weapon;

        if (weapon is null)
            return null;

        weapon.EquipAvatarId = 0;
        Weapon = null;
        WeaponItemId = 0;
        WeaponGadgetId = 0;
        WeaponGuid = 0;
        return weapon;
    }

    internal void Recalculate() => RecalculateFightProperties();

    internal bool SetSkillDepot(uint skillDepotId)
    {
        if (skillDepotId == SkillDepotId || !IsCandidateSkillDepot(skillDepotId))
            return false;

        SkillDepotId = skillDepotId;
        EnsureCurrentDepotSkills();
        RecalculateFightProperties();
        return true;
    }

    public float GetFightProperty(FightProperty property) => FightPropertyStore.Get(property);

    public uint GetSkillLevel(uint skill) => _skillLevelMap.GetValueOrDefault(skill, defaultValue: 1u);

    public IReadOnlyDictionary<uint, uint> GetActiveSkillLevelMap() =>
        SkillDepot.SkillsAndEnergySkill.ToDictionary(skill => skill, GetSkillLevel);

    public IReadOnlyDictionary<uint, uint> GetProudSkillExtraLevelMap()
    {
        var rawBonuses = new Dictionary<uint, uint>();

        foreach (var talentId in Talents)
        {
            if (!_data.AvatarTalentData.TryGetValue(talentId, out var talent) || string.IsNullOrEmpty(talent.ConfigName))
                continue;

            foreach (var entry in _data.ResolveTalent(talent.ConfigName))
            {
                if (!string.Equals(entry.Type, "AddTalentExtraLevel", StringComparison.Ordinal) ||
                    entry.TalentIndex == 0 || entry.ExtraLevel == 0)
                    continue;

                uint skillId = entry.TalentIndex switch {
                    9 => SkillDepot.EnergySkill,
                    2 when SkillDepot.Skills.Count >= 2 => SkillDepot.Skills[1],
                    1 when SkillDepot.Skills.Count >= 1 => SkillDepot.Skills[0],
                    _ => 0
                };

                if (skillId == 0 || !_data.AvatarSkillData.TryGetValue(skillId, out var skill) || skill.ProudSkillGroupId == 0)
                    continue;

                rawBonuses[skill.ProudSkillGroupId] = rawBonuses.GetValueOrDefault(skill.ProudSkillGroupId) + entry.ExtraLevel;
            }
        }

        var result = new Dictionary<uint, uint>();

        foreach (var skillId in SkillDepot.SkillsAndEnergySkill)
        {
            if (!_data.AvatarSkillData.TryGetValue(skillId, out var skill) || skill.ProudSkillGroupId == 0)
                continue;

            var bonus = rawBonuses.GetValueOrDefault(skill.ProudSkillGroupId);
            var maxLevel = _data.GetProudSkillGroupMaxLevel(skill.ProudSkillGroupId);
            var currentLevel = GetSkillLevel(skillId);

            if (maxLevel != 0)
                bonus = Math.Min(bonus, maxLevel > currentLevel ? maxLevel - currentLevel : 0);

            result[skill.ProudSkillGroupId] = bonus;
        }

        return result;
    }

    public IReadOnlyDictionary<uint, uint> GetSkillExtraChargeMap()
    {
        var result = new Dictionary<uint, uint>();

        foreach (var talentId in Talents)
        {
            if (!_data.AvatarTalentData.TryGetValue(talentId, out var talent) || string.IsNullOrEmpty(talent.ConfigName))
                continue;

            foreach (var entry in _data.ResolveTalent(talent.ConfigName))
            {
                if (!string.Equals(entry.Type, "ModifySkillPoint", StringComparison.Ordinal) || entry.SkillId == 0 ||
                    !_data.AvatarSkillData.TryGetValue(entry.SkillId, out var skill))
                    continue;

                var charge = Math.Max(val1: 0L, skill.MaxChargeNum + entry.PointDelta);
                result[entry.SkillId] = (uint)charge;
            }
        }

        return result;
    }

    public IReadOnlyList<uint> GetInherentProudSkillList() => [
        .. GetProudSkillList().Where(id =>
            _data.ProudSkills.TryGetValue(id, out var proudSkill) &&
            (string.IsNullOrEmpty(proudSkill.DisplayType) ||
             string.Equals(proudSkill.DisplayType, "PROUD_SKILL_DISPLAY_DEFAULT", StringComparison.Ordinal) ||
             string.Equals(proudSkill.DisplayType, "PROUD_SKILL_DISPLAY_BREAK", StringComparison.Ordinal)))
    ];

    public IReadOnlyList<uint> GetSpecialProudSkillList() => [
        .. GetProudSkillList().Where(id =>
            _data.ProudSkills.TryGetValue(id, out var proudSkill) &&
            (string.Equals(proudSkill.DisplayType, "PROUD_SKILL_DISPLAY_EXTRA", StringComparison.Ordinal) ||
             string.Equals(proudSkill.DisplayType, "PROUD_SKILL_DISPLAY_AQ_QUEST_UNLOCK", StringComparison.Ordinal)))
    ];

    public AvatarInfo Info()
    {
        var info = new AvatarInfo {
            AvatarType = AvatarTypeFormal,
            AvatarId = AvatarId,
            Guid = Guid,
            LifeState = GetFightProperty(FightProperty.FIGHT_PROP_CUR_HP) <= 0f ? Dead : Alive,
            SkillDepotId = SkillDepotId,
            BornTime = BornTime,
            WearingFlycloakId = DefaultFlycloak,
            EquipGuidList = [WeaponGuid],
            CoreProudSkillLevel = CoreProudSkillLevel,
            FetterInfo = new AvatarFetterInfo { ExpLevel = 1 },
            PropMap = {
                [(uint)PlayerProperty.Exp] = PlayerProperty.Exp.Value(0),
                [(uint)PlayerProperty.Level] = PlayerProperty.Level.Value(Level),
                [(uint)PlayerProperty.BreakLevel] = PlayerProperty.BreakLevel.Value(PromoteLevel),
                [(uint)PlayerProperty.SatiationVal] = PlayerProperty.SatiationVal.Value(0),
                [(uint)PlayerProperty.SatiationPenaltyTime] = PlayerProperty.SatiationPenaltyTime.Value(0)
            },
            TalentIdList = [.. Talents],
            InherentProudSkillList = [.. GetInherentProudSkillList()],
            SpecialProudSkillList = [.. GetSpecialProudSkillList()],
            CandSkillDepotIdList = [.. CandSkillDepotIds]
        };

        foreach (var (property, value) in FightPropertyStore)
        {
            info.FightPropMap[property] = value;
        }

        foreach (var (skill, skillLevel) in GetActiveSkillLevelMap())
        {
            info.SkillLevelMap[skill] = skillLevel;
        }

        foreach (var (group, bonus) in GetProudSkillExtraLevelMap())
        {
            info.ProudSkillExtraLevelMap[group] = bonus;
        }

        foreach (var (skill, charge) in GetSkillExtraChargeMap())
        {
            info.SkillMap[skill] = new AvatarSkillInfo { MaxChargeCount = charge };
        }

        return info;
    }

    public void PopulateSceneProgression(SceneAvatarInfo info)
    {
        info.CoreProudSkillLevel = CoreProudSkillLevel;
        info.TalentIdList.AddRange(Talents);
        info.InherentProudSkillList.AddRange(GetInherentProudSkillList());
        info.SpecialProudSkillList.AddRange(GetSpecialProudSkillList());
        info.CandSkillDepotIdList.AddRange(CandSkillDepotIds);

        foreach (var (skill, level) in GetActiveSkillLevelMap())
        {
            info.SkillLevelMap[skill] = level;
        }

        foreach (var (group, bonus) in GetProudSkillExtraLevelMap())
        {
            info.ProudSkillExtraLevelMap[group] = bonus;
        }
    }

    private void EnsureCurrentDepotSkills()
    {
        foreach (var skill in SkillDepot.SkillsAndEnergySkill)
        {
            _skillLevelMap.TryAdd(skill, value: 1);
        }
    }

    private bool IsCandidateSkillDepot(uint skillDepotId) =>
        skillDepotId == AvatarConfig.SkillDepotId || AvatarConfig.CandSkillDepotIds.Contains(skillDepotId);

    private uint GetCoreProudSkillLevel()
    {
        var locked = SkillDepot.Talents
            .Where(id => id != 0 && !_talentIds.Contains(id))
            .Select(id => id % 10)
            .Where(index => index != 0)
            .ToArray();

        if (locked.Length == 0)
            return 6;

        var firstLocked = locked.Min();
        return firstLocked == 0 ? 0 : firstLocked - 1;
    }

    private IReadOnlyList<uint> GetProudSkillList()
    {
        var list = new List<uint>();

        foreach (var open in SkillDepot.InherentProudSkillOpens)
        {
            if (open.ProudSkillGroupId == 0 || open.NeedAvatarPromoteLevel > PromoteLevel)
                continue;

            var id = open.ProudSkillGroupId * 100 + 1;

            if (_data.ProudSkills.ContainsKey(id))
                list.Add(id);
        }

        foreach (var open in SkillDepot.SpecialProudSkillOpens)
        {
            if (open.ProudSkillGroupId == 0)
                continue;

            var id = open.ProudSkillGroupId * 100 + 1;

            if (_data.ProudSkills.ContainsKey(id))
                list.Add(id);
        }

        return [.. list.Distinct()];
    }

    private void RecalculateFightProperties()
    {
        var oldMaxHp = GetFightProperty(FightProperty.FIGHT_PROP_MAX_HP);
        var oldCurHp = GetFightProperty(FightProperty.FIGHT_PROP_CUR_HP);
        var hpRatio = oldMaxHp > 0 ? Math.Clamp(oldCurHp / oldMaxHp, min: 0f, max: 1f) : 1f;
        var oldHpDebt = GetFightProperty(FightProperty.FIGHT_PROP_CUR_HP_DEBTS);
        var oldEnergy = GetCurrentEnergyProperty() is {} currentEnergy ? GetFightProperty(currentEnergy) : 0f;

        FightPropertyStore.Clear();

        AddFightProperty(
            FightProperty.FIGHT_PROP_BASE_HP,
            AvatarConfig.GetBaseStat(FightProperty.FIGHT_PROP_BASE_HP, Level, _data.AvatarCurveData));

        AddFightProperty(
            FightProperty.FIGHT_PROP_BASE_ATTACK,
            AvatarConfig.GetBaseStat(FightProperty.FIGHT_PROP_BASE_ATTACK, Level, _data.AvatarCurveData));

        AddFightProperty(
            FightProperty.FIGHT_PROP_BASE_DEFENSE,
            AvatarConfig.GetBaseStat(FightProperty.FIGHT_PROP_BASE_DEFENSE, Level, _data.AvatarCurveData));
        AddFightProperty(FightProperty.FIGHT_PROP_CRITICAL, AvatarConfig.CritChanceBase);
        AddFightProperty(FightProperty.FIGHT_PROP_CRITICAL_HURT, AvatarConfig.CritDamageBase);
        AddFightProperty(FightProperty.FIGHT_PROP_ELEMENT_MASTERY, AvatarConfig.ElementMasteryBase);
        AddFightProperty(FightProperty.FIGHT_PROP_CHARGE_EFFICIENCY, AvatarConfig.ChargeEfficiencyBase);
        SetFightProperty(FightProperty.FIGHT_PROP_CUR_HP_DEBTS, oldHpDebt);

        AddFightProperties(_data.ResolveAvatarPromote(AvatarConfig.AvatarPromoteId, PromoteLevel)?.AddProps);

        foreach (var proudSkillId in GetProudSkillList())
        {
            if (_data.ProudSkills.TryGetValue(proudSkillId, out var proudSkill))
                AddFightProperties(proudSkill.AddProps);
        }

        AddWeaponFightProperties();

        SetFightProperty(
            FightProperty.FIGHT_PROP_MAX_HP,
            GetFightProperty(FightProperty.FIGHT_PROP_HP) +
            GetFightProperty(FightProperty.FIGHT_PROP_BASE_HP) *
            (1f + GetFightProperty(FightProperty.FIGHT_PROP_HP_PERCENT)));

        SetFightProperty(
            FightProperty.FIGHT_PROP_CUR_ATTACK,
            GetFightProperty(FightProperty.FIGHT_PROP_ATTACK) +
            GetFightProperty(FightProperty.FIGHT_PROP_BASE_ATTACK) *
            (1f + GetFightProperty(FightProperty.FIGHT_PROP_ATTACK_PERCENT)));

        SetFightProperty(
            FightProperty.FIGHT_PROP_CUR_DEFENSE,
            GetFightProperty(FightProperty.FIGHT_PROP_DEFENSE) +
            GetFightProperty(FightProperty.FIGHT_PROP_BASE_DEFENSE) *
            (1f + GetFightProperty(FightProperty.FIGHT_PROP_DEFENSE_PERCENT)));
        SetFightProperty(FightProperty.FIGHT_PROP_CUR_HP, GetFightProperty(FightProperty.FIGHT_PROP_MAX_HP) * hpRatio);

        SetEnergyProperties(oldEnergy);
    }

    private void AddWeaponFightProperties()
    {
        if (!_data.WeaponData.TryGetValue(WeaponItemId, out var weaponData))
            return;

        var level = Weapon?.Level ?? 1u;
        var promoteLevel = Weapon?.PromoteLevel ?? 0u;
        var refinement = Weapon?.Refinement ?? 1u;
        var affixId = Weapon?.AffixId ?? weaponData.SkillAffix.FirstOrDefault();

        _data.WeaponCurveData.TryGetValue(level, out var curve);

        foreach (var property in weaponData.WeaponProperties)
        {
            if (!property.TryGetProperty(out var fightProperty))
                continue;

            AddFightProperty(fightProperty, property.InitValue * (curve?.GetMultiplier(property.CurveType) ?? 1f));
        }

        AddFightProperties(_data.ResolveWeaponPromote(weaponData.WeaponPromoteId, promoteLevel)?.AddProps);

        if (affixId != 0 && _data.ResolveEquipAffix(affixId, refinement) is {} affix)
            AddFightProperties(affix.AddProps);
    }

    private void AddFightProperties(IEnumerable<FightPropData>? properties)
    {
        if (properties is null)
            return;

        foreach (var property in properties)
        {
            if (property.Value != 0 && property.TryGetProperty(out var fightProperty))
                AddFightProperty(fightProperty, property.Value);
        }
    }

    private void AddFightProperty(FightProperty property, float value) =>
        FightPropertyStore.Add(property, value);

    private void SetFightProperty(FightProperty property, float value) => FightPropertyStore.Set(property, value);

    private FightProperty? GetCurrentEnergyProperty()
    {
        if (!_data.AvatarSkillData.TryGetValue(SkillDepot.EnergySkill, out var skill))
            return null;

        if (skill.HasSpecialEnergyRequirement)
            return FightProperty.FIGHT_PROP_CUR_SPECIAL_ENERGY;

        return skill.CostElemType switch {
            "Fire" => FightProperty.FIGHT_PROP_CUR_FIRE_ENERGY,
            "Electric" or "Elec" => FightProperty.FIGHT_PROP_CUR_ELEC_ENERGY,
            "Water" => FightProperty.FIGHT_PROP_CUR_WATER_ENERGY,
            "Grass" => FightProperty.FIGHT_PROP_CUR_GRASS_ENERGY,
            "Wind" => FightProperty.FIGHT_PROP_CUR_WIND_ENERGY,
            "Ice" => FightProperty.FIGHT_PROP_CUR_ICE_ENERGY,
            "Rock" => FightProperty.FIGHT_PROP_CUR_ROCK_ENERGY,
            _ => null
        };
    }

    private void SetEnergyProperties(float currentEnergy)
    {
        if (!_data.AvatarSkillData.TryGetValue(SkillDepot.EnergySkill, out var skill))
            return;

        if (skill.HasSpecialEnergyRequirement)
        {
            SetFightProperty(FightProperty.FIGHT_PROP_START_SPECIAL_ENERGY, skill.EnergyStart);
            SetFightProperty(FightProperty.FIGHT_PROP_MAX_SPECIAL_ENERGY, skill.EnergyMaximum);
            SetFightProperty(FightProperty.FIGHT_PROP_CUR_SPECIAL_ENERGY, Math.Clamp(currentEnergy, min: 0f, skill.EnergyMaximum));
            return;
        }

        var properties = skill.CostElemType switch {
            "Fire" => (FightProperty.FIGHT_PROP_MAX_FIRE_ENERGY, FightProperty.FIGHT_PROP_CUR_FIRE_ENERGY),
            "Electric" or "Elec" => (FightProperty.FIGHT_PROP_MAX_ELEC_ENERGY, FightProperty.FIGHT_PROP_CUR_ELEC_ENERGY),
            "Water" => (FightProperty.FIGHT_PROP_MAX_WATER_ENERGY, FightProperty.FIGHT_PROP_CUR_WATER_ENERGY),
            "Grass" => (FightProperty.FIGHT_PROP_MAX_GRASS_ENERGY, FightProperty.FIGHT_PROP_CUR_GRASS_ENERGY),
            "Wind" => (FightProperty.FIGHT_PROP_MAX_WIND_ENERGY, FightProperty.FIGHT_PROP_CUR_WIND_ENERGY),
            "Ice" => (FightProperty.FIGHT_PROP_MAX_ICE_ENERGY, FightProperty.FIGHT_PROP_CUR_ICE_ENERGY),
            "Rock" => (FightProperty.FIGHT_PROP_MAX_ROCK_ENERGY, FightProperty.FIGHT_PROP_CUR_ROCK_ENERGY),
            _ => (FightProperty.FIGHT_PROP_NONE, FightProperty.FIGHT_PROP_NONE)
        };

        if (properties.Item1 == FightProperty.FIGHT_PROP_NONE)
            return;

        SetFightProperty(properties.Item1, skill.EnergyMaximum);
        SetFightProperty(properties.Item2, Math.Clamp(currentEnergy, min: 0f, skill.EnergyMaximum));
    }

    private static uint ResolveSkillDepotId(GameData data, AvatarData avatar, uint requested)
    {
        if (requested != 0 && data.AvatarSkillDepotData.ContainsKey(requested) &&
            (requested == avatar.SkillDepotId || avatar.CandSkillDepotIds.Contains(requested)))
            return requested;

        return avatar.SkillDepotId;
    }

    private static uint PromoteLevelFor(uint level) => level switch {
        > 80 => 6,
        > 70 => 5,
        > 60 => 4,
        > 50 => 3,
        > 40 => 2,
        > 20 => 1,
        _ => 0
    };
}
