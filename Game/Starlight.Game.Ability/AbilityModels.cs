using Starlight.Game.Resources;
using Starlight.Game.Resources.Binary;

namespace Starlight.Game.Ability;

public static class AbilityHash
{
    public static uint Compute(string value) => AbilityResourceHash.Compute(value);
}

public readonly struct AbilityKey : IEquatable<AbilityKey>
{
    public static readonly AbilityKey Default = FromName("Default");

    private AbilityKey(uint hash, string? name)
    {
        Hash = hash;
        Name = name;
    }

    public uint Hash { get; }
    public string? Name { get; }
    public bool IsHash => Name is null;

    public static AbilityKey FromName(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        return new AbilityKey(AbilityHash.Compute(name), name);
    }

    public static AbilityKey FromHash(uint hash) => new(hash, name: null);

    public bool Equals(AbilityKey other) => Hash == other.Hash;
    public override bool Equals(object? obj) => obj is AbilityKey other && Equals(other);
    public override int GetHashCode() => Hash.GetHashCode();
    public static bool operator ==(AbilityKey left, AbilityKey right) => left.Equals(right);
    public static bool operator !=(AbilityKey left, AbilityKey right) => !left.Equals(right);
    public override string ToString() => Name ?? $"0x{Hash:X8}";
}

public enum AbilityScalarKind
{
    Unknown,
    Float,
    Int,
    Bool,
    Trigger,
    String,
    UInt
}

public readonly record struct AbilityScalarValue(
    AbilityScalarKind Kind,
    float FloatValue = 0,
    int IntValue = 0,
    string? StringValue = null,
    uint UIntValue = 0
)
{
    public static AbilityScalarValue Unknown() => new(AbilityScalarKind.Unknown);
    public static AbilityScalarValue FromFloat(float value) => new(AbilityScalarKind.Float, value);
    public static AbilityScalarValue FromInt(int value) => new(AbilityScalarKind.Int, IntValue: value);
    public static AbilityScalarValue FromBool(bool value) => new(AbilityScalarKind.Bool, IntValue: value ? 1 : 0);
    public static AbilityScalarValue Trigger() => new(AbilityScalarKind.Trigger);
    public static AbilityScalarValue FromString(string value) => new(AbilityScalarKind.String, StringValue: value);
    public static AbilityScalarValue FromUInt(uint value) => new(AbilityScalarKind.UInt, UIntValue: value);
}

public enum AbilityOwnerType
{
    Unknown,
    Avatar,
    Weapon,
    Monster,
    Gadget,
    ClientGadget,
    Team,
    Scene,
    MpLevel,
    Npc,
    Vehicle,
    Region,
    Other
}

public readonly record struct AbilityOwner(
    uint EntityId,
    AbilityOwnerType Type,
    uint AuthorityPeerId = 0,
    uint PlayerUid = 0,
    int ClientInitInvokeLimit = 0
)
{
    public int EffectiveClientInitInvokeLimit => ClientInitInvokeLimit > 0 ? ClientInitInvokeLimit
        : Type == AbilityOwnerType.Team ? 500 : 50;
}

public readonly record struct AbilityEmbryoSeed(string Name, string Override = "Default");

public enum AbilityInstanceOrigin
{
    Client,
    Server
}

public readonly record struct AbilitySpecialAdjustment(float Delta, float Ratio)
{
    public AbilitySpecialAdjustment Add(float delta, float ratio) => new(Delta + delta, Ratio + ratio);
    public float Apply(float value) => (value + Delta) * (1f + Ratio);
}

public sealed record AbilityEmbryoState(uint AbilityId, AbilityKey Name, AbilityKey Override);

public sealed class AbilityInstance(
    uint instancedAbilityId,
    AbilityKey name,
    AbilityKey? @override = null,
    AbilityInstanceOrigin origin = AbilityInstanceOrigin.Client
)
{
    private readonly Dictionary<AbilityKey, AbilityScalarValue> _overrides = [];

    public uint InstancedAbilityId { get; } = instancedAbilityId;
    public AbilityKey Name { get; internal set; } = name;
    public AbilityKey Override { get; internal set; } = @override ?? AbilityKey.Default;
    public AbilityInstanceOrigin Origin { get; internal set; } = origin;
    public AbilityConfig? Definition { get; internal set; }
    public IReadOnlyDictionary<AbilityKey, AbilityScalarValue> Overrides => _overrides;

    public void SetOverride(AbilityKey key, AbilityScalarValue value) => _overrides[key] = value;
    public bool ClearOverride(AbilityKey key) => _overrides.Remove(key);

    public void ReinitializeOverrides(IEnumerable<KeyValuePair<AbilityKey, AbilityScalarValue>> values)
    {
        _overrides.Clear();

        foreach (var (key, value) in values)
        {
            _overrides[key] = value;
        }
    }
}

public sealed class AbilityModifierInstance(
    uint instancedModifierId,
    uint instancedAbilityId,
    int modifierLocalId,
    uint parentAbilityEntityId,
    AbilityKey parentAbilityName,
    AbilityKey parentAbilityOverride
)
{
    public uint InstancedModifierId { get; } = instancedModifierId;
    public uint InstancedAbilityId { get; } = instancedAbilityId;
    public int ModifierLocalId { get; } = modifierLocalId;
    public uint ParentAbilityEntityId { get; set; } = parentAbilityEntityId;
    public AbilityKey ParentAbilityName { get; set; } = parentAbilityName;
    public AbilityKey ParentAbilityOverride { get; set; } = parentAbilityOverride;
    public string? ModifierName { get; set; }
    public float ExistDuration { get; set; }
    public uint ApplyEntityId { get; set; }
    public bool IsAttachedParentAbility { get; set; }
    public bool HasAttachedModifier { get; set; }
    public bool AttachedModifierInvalid { get; set; }
    public uint AttachedModifierOwnerEntityId { get; set; }
    public uint AttachedInstancedModifierId { get; set; }
    public bool AttachedIsServerBuffModifier { get; set; }
    public int AttachedNameHash { get; set; }
    public bool HasDurability { get; set; }
    public float ReduceRatio { get; set; }
    public float RemainingDurability { get; set; }
    public bool IsDurabilityZero { get; set; }
    public uint ServerBuffUid { get; set; }
    public bool IsServerBuffModifier { get; set; }
    public bool ExtraFlag { get; set; }
}
