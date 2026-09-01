using Starlight.Game.Resources.Binary;

namespace Starlight.Game.Ability;

public sealed class AbilityComponent
{
    private readonly List<AbilityEmbryoState> _embryos = [];
    private readonly SortedDictionary<uint, AbilityInstance> _appliedAbilities = [];
    private readonly SortedDictionary<uint, AbilityModifierInstance> _appliedModifiers = [];
    private readonly Dictionary<AbilityKey, AbilityScalarValue> _dynamicValues = [];
    private readonly Dictionary<AbilityKey, AbilityScalarValue> _serverGlobalValues = [];
    private readonly Dictionary<uint, float> _fightProperties = [];
    private readonly Dictionary<AbilityKey, Dictionary<AbilityKey, AbilitySpecialAdjustment>> _targetAbilitySpecials = [];
    private readonly HashSet<string> _limitedHpDebtTags = new(StringComparer.Ordinal);
    private readonly HashSet<uint> _hpDebtLimitModifierIds = [];
    private float _maxHpDebtLimit = float.PositiveInfinity;
    private uint _lastEmbryoId;
    private uint _lastServerAbilityId;

    public AbilityComponent(AbilityOwner owner)
    {
        if (owner.EntityId == 0)
            throw new ArgumentOutOfRangeException(nameof(owner));

        Owner = owner;
    }

    public AbilityOwner Owner { get; private set; }
    public bool IsServerInitialized { get; private set; } = true;
    public bool IsClientInitialized { get; private set; }
    public bool IsKilled { get; private set; }
    public IReadOnlyList<AbilityEmbryoState> Embryos => _embryos;
    public IReadOnlyDictionary<uint, AbilityInstance> AppliedAbilities => _appliedAbilities;
    public IReadOnlyDictionary<uint, AbilityModifierInstance> AppliedModifiers => _appliedModifiers;
    public IReadOnlyDictionary<AbilityKey, AbilityScalarValue> DynamicValues => _dynamicValues;
    public IReadOnlyDictionary<AbilityKey, AbilityScalarValue> ServerGlobalValues => _serverGlobalValues;
    public IReadOnlyDictionary<uint, float> FightProperties => _fightProperties;
    public IReadOnlyDictionary<AbilityKey, Dictionary<AbilityKey, AbilitySpecialAdjustment>> TargetAbilitySpecials =>
        _targetAbilitySpecials;

    public void UpdateOwner(AbilityOwner owner)
    {
        if (owner.EntityId != Owner.EntityId)
            throw new ArgumentException("Entity ID cannot change on an existing ability component.", nameof(owner));

        Owner = owner;
    }

    public void ResetEmbryos(IEnumerable<string> abilityNames) =>
        ResetEmbryos(abilityNames.Select(name => new AbilityEmbryoSeed(name)));

    public void ResetEmbryos(IEnumerable<AbilityEmbryoSeed> abilities)
    {
        _embryos.Clear();
        _lastEmbryoId = 0;

        foreach (var ability in abilities)
        {
            AddEmbryo(ability.Name, string.IsNullOrEmpty(ability.Override) ? "Default" : ability.Override);
        }
    }

    public AbilityEmbryoState AddEmbryo(string name, string overrideName = "Default")
    {
        var embryo = new AbilityEmbryoState(
            ++_lastEmbryoId,
            AbilityKey.FromName(name),
            AbilityKey.FromName(string.IsNullOrEmpty(overrideName) ? "Default" : overrideName));
        _embryos.Add(embryo);
        return embryo;
    }

    public bool RemoveEmbryo(uint abilityId)
    {
        var index = _embryos.FindIndex(x => x.AbilityId == abilityId);

        if (index < 0)
            return false;

        _embryos.RemoveAt(index);
        return true;
    }

    public AbilityInstance UpsertAbility(
        uint instancedAbilityId,
        AbilityKey name,
        AbilityKey? @override = null,
        AbilityConfig? definition = null,
        AbilityInstanceOrigin origin = AbilityInstanceOrigin.Client
    )
    {
        if (instancedAbilityId == 0)
            throw new ArgumentOutOfRangeException(nameof(instancedAbilityId));

        if (!_appliedAbilities.TryGetValue(instancedAbilityId, out var ability))
        {
            ability = new AbilityInstance(instancedAbilityId, name, @override, origin);
            _appliedAbilities.Add(instancedAbilityId, ability);
        } else
        {
            ability.Name = name;
            ability.Override = @override ?? AbilityKey.Default;
            ability.Origin = origin;
        }

        if (definition is not null)
            ability.Definition = definition;

        if (origin == AbilityInstanceOrigin.Server)
            _lastServerAbilityId = Math.Max(_lastServerAbilityId, instancedAbilityId);

        return ability;
    }

    public AbilityInstance? AddServerAbility(
        string name,
        string overrideName,
        AbilityConfig? definition
    )
    {
        if (string.IsNullOrEmpty(name) || definition is null)
            return null;

        var key = AbilityKey.FromName(name);

        if (_appliedAbilities.Values.Any(ability => ability.Origin == AbilityInstanceOrigin.Server && ability.Name == key))
            return null;

        uint id;

        do
        {
            id = ++_lastServerAbilityId;
        } while (_appliedAbilities.ContainsKey(id));

        return UpsertAbility(
            id,
            key,
            AbilityKey.FromName(string.IsNullOrEmpty(overrideName) ? "Default" : overrideName),
            definition,
            AbilityInstanceOrigin.Server);
    }

    public bool TryGetAbility(uint instancedAbilityId, out AbilityInstance ability) =>
        _appliedAbilities.TryGetValue(instancedAbilityId, out ability!);

    public bool RemoveAbility(uint instancedAbilityId)
    {
        if (!_appliedAbilities.ContainsKey(instancedAbilityId))
            return false;

        // Modifier removal has lifecycle side effects (including HP-debt limit reset)
        foreach (var modifierId in _appliedModifiers
                     .Where(pair => pair.Value.InstancedAbilityId == instancedAbilityId)
                     .Select(pair => pair.Key)
                     .ToArray())
        {
            RemoveModifier(modifierId);
        }

        return _appliedAbilities.Remove(instancedAbilityId);
    }

    public void ResetServerAbilities()
    {
        foreach (var id in _appliedAbilities
                     .Where(pair => pair.Value.Origin == AbilityInstanceOrigin.Server)
                     .Select(pair => pair.Key)
                     .ToArray())
        {
            RemoveAbility(id);
        }

        _lastServerAbilityId = 0;
    }

    public AbilityModifierInstance UpsertModifier(AbilityModifierInstance modifier)
    {
        if (modifier.InstancedModifierId == 0)
            throw new ArgumentOutOfRangeException(nameof(modifier));

        if (_appliedModifiers.TryGetValue(modifier.InstancedModifierId, out var previous) &&
            !string.Equals(previous.ModifierName, modifier.ModifierName, StringComparison.Ordinal))
            RemoveModifier(modifier.InstancedModifierId);

        _appliedModifiers[modifier.InstancedModifierId] = modifier;
        return modifier;
    }

    public bool TryGetModifier(uint instancedModifierId, out AbilityModifierInstance modifier) =>
        _appliedModifiers.TryGetValue(instancedModifierId, out modifier!);

    public bool RemoveModifier(uint instancedModifierId)
    {
        if (!_appliedModifiers.TryGetValue(instancedModifierId, out var modifier))
            return false;

        if (_hpDebtLimitModifierIds.Remove(instancedModifierId) ||
            ModifierHasMixin(modifier, "LimitHpDebtsByTagMixin"))
            ResetHpDebtLimit();

        return _appliedModifiers.Remove(instancedModifierId);
    }

    public float GetFightProperty(uint property) => _fightProperties.GetValueOrDefault(property);

    public void SetFightProperty(uint property, float value) => _fightProperties[property] = value;

    internal void SetHpDebtLimit(float ratio, IEnumerable<string> hpDebtTags)
    {
        _maxHpDebtLimit =
            GetFightProperty(DynamicProps.AbilityFightProperty.MaxHp) * ratio +
            GetFightProperty(DynamicProps.AbilityFightProperty.CurHpDebts);

        _limitedHpDebtTags.Clear();

        foreach (var tag in hpDebtTags)
        {
            _limitedHpDebtTags.Add(tag);
        }
    }

    internal void ResetHpDebtLimit() => SetHpDebtLimit(ratio: 2f, Array.Empty<string>());

    internal void MarkHpDebtLimitModifier(uint instancedModifierId)
    {
        if (instancedModifierId != 0)
            _hpDebtLimitModifierIds.Add(instancedModifierId);
    }

    internal float GetHpDebtMaximum(string hpDebtTag)
    {
        var defaultMaximum = GetFightProperty(DynamicProps.AbilityFightProperty.MaxHp) * 2f;
        return _limitedHpDebtTags.Contains(hpDebtTag) ? Math.Min(defaultMaximum, _maxHpDebtLimit) : defaultMaximum;
    }

    private bool ModifierHasMixin(AbilityModifierInstance modifier, string mixinType)
    {
        if (modifier.ModifierName is not { Length: > 0 } modifierName ||
            !TryGetAbility(modifier.InstancedAbilityId, out var ability) ||
            ability.Definition is null ||
            !ability.Definition.Modifiers.TryGetValue(modifierName, out var config))
            return false;

        return config.ModifierMixins.Any(mixin => string.Equals(mixin.Type, mixinType, StringComparison.Ordinal));
    }

    public void ReinitializeFightProperties(IEnumerable<KeyValuePair<uint, float>> values)
    {
        _fightProperties.Clear();

        foreach (var (property, value) in values)
        {
            _fightProperties[property] = value;
        }
    }

    public void SetDynamicValue(AbilityKey key, AbilityScalarValue value) => _dynamicValues[key] = value;
    public bool ClearDynamicValue(AbilityKey key) => _dynamicValues.Remove(key);
    public void SetServerGlobalValue(AbilityKey key, AbilityScalarValue value) => _serverGlobalValues[key] = value;
    public bool ClearServerGlobalValue(AbilityKey key) => _serverGlobalValues.Remove(key);
    public void ClearServerGlobalValues() => _serverGlobalValues.Clear();
    public void SetKilled(bool killed) => IsKilled = killed;
    public void MarkClientInitialized() => IsClientInitialized = true;
    public void MarkServerInitialized() => IsServerInitialized = true;

    public void ClearTargetAbilitySpecials() => _targetAbilitySpecials.Clear();

    public void AddTargetAbilitySpecial(AbilityKey ability, AbilityKey param, float delta, float ratio)
    {
        if (!_targetAbilitySpecials.TryGetValue(ability, out var parameters))
            _targetAbilitySpecials[ability] = parameters = [];

        parameters[param] = parameters.TryGetValue(param, out var current) ?
            current.Add(delta, ratio) :
            new AbilitySpecialAdjustment(delta, ratio);
    }

    public bool TryApplyTargetAbilitySpecial(AbilityKey ability, AbilityKey param, float value, out float result)
    {
        if (_targetAbilitySpecials.TryGetValue(ability, out var parameters) &&
            parameters.TryGetValue(param, out var adjustment))
        {
            result = adjustment.Apply(value);
            return true;
        }

        result = value;
        return false;
    }

    public void ResetClientState()
    {
        IsClientInitialized = false;

        foreach (var modifierId in _appliedModifiers.Keys.ToArray())
        {
            RemoveModifier(modifierId);
        }

        foreach (var id in _appliedAbilities
                     .Where(pair => pair.Value.Origin == AbilityInstanceOrigin.Client)
                     .Select(pair => pair.Key)
                     .ToArray())
        {
            _appliedAbilities.Remove(id);
        }
    }
}
