using Starlight.Protocol;

namespace Starlight.Game.Ability;

public static class AbilityProtocol
{
    public static AbilityControlBlock ToControlBlock(AbilityComponent component)
    {
        var block = new AbilityControlBlock();

        foreach (var embryo in component.Embryos)
        {
            block.AbilityEmbryoList.Add(new AbilityEmbryo {
                AbilityId = embryo.AbilityId,
                AbilityNameHash = embryo.Name.Hash,
                AbilityOverrideNameHash = embryo.Override.Hash
            });
        }

        return block;
    }

    public static AbilitySyncStateInfo ToSyncState(AbilityComponent component)
    {
        var info = new AbilitySyncStateInfo { IsInited = component.IsClientInitialized };

        foreach (var (key, value) in Order(component.DynamicValues))
        {
            info.DynamicValueMap.Add(ToScalarEntry(key, value));
        }

        foreach (var ability in component.AppliedAbilities.Values)
        {
            if (component.Owner.Type is not (AbilityOwnerType.Avatar or AbilityOwnerType.Team) &&
                ability.Overrides.Count == 0)
                continue;

            var applied = new AbilityAppliedAbility {
                InstancedAbilityId = ability.InstancedAbilityId,
                AbilityName = ToAbilityString(ability.Name),
                AbilityOverride = ToAbilityString(ability.Override)
            };

            foreach (var (key, value) in Order(ability.Overrides))
            {
                applied.OverrideMap.Add(ToScalarEntry(key, value));
            }

            info.AppliedAbilities.Add(applied);
        }

        foreach (var modifier in component.AppliedModifiers.Values)
        {
            info.AppliedModifiers.Add(ToAppliedModifier(modifier));
        }

        foreach (var (key, value) in Order(component.ServerGlobalValues))
        {
            info.SgvDynamicValueMap.Add(ToScalarEntry(key, value));
        }

        return info;
    }

    public static AbilityString ToAbilityString(AbilityKey key) =>
        key == AbilityKey.Default ? new AbilityString() : new AbilityString { Hash = key.Hash };

    public static AbilityKey FromAbilityString(AbilityString? value)
    {
        if (value is null)
            return AbilityKey.Default;

        return value.TypeCase switch {
            AbilityString.TypeOneofCase.Str when value.Str.Length > 0 => AbilityKey.FromName(value.Str),
            AbilityString.TypeOneofCase.Hash => AbilityKey.FromHash(value.Hash),
            _ => AbilityKey.Default
        };
    }

    public static AbilityScalarValueEntry ToScalarEntry(AbilityKey key, AbilityScalarValue value)
    {
        var entry = new AbilityScalarValueEntry {
            Key = ToAbilityString(key),
            ValueType = ToScalarType(value.Kind)
        };

        switch (value.Kind)
        {
            case AbilityScalarKind.Float:
                entry.FloatValue = value.FloatValue;
                break;
            case AbilityScalarKind.Int:
            case AbilityScalarKind.Bool:
                entry.IntValue = value.IntValue;
                break;
            case AbilityScalarKind.Trigger:
                break;
            case AbilityScalarKind.String:
                entry.StringValue = value.StringValue ?? string.Empty;
                break;
            case AbilityScalarKind.UInt:
                entry.UintValue = value.UIntValue;
                break;
        }

        return entry;
    }

    public static AbilityScalarValue FromScalarEntry(AbilityScalarValueEntry value) =>
        value.ValueType switch {
            AbilityScalarType.ABILITY_SCALAR_TYPE_FLOAT => AbilityScalarValue.FromFloat(value.FloatValue),
            AbilityScalarType.ABILITY_SCALAR_TYPE_INT => AbilityScalarValue.FromInt(value.IntValue),
            AbilityScalarType.ABILITY_SCALAR_TYPE_BOOL => AbilityScalarValue.FromBool(value.IntValue != 0),
            AbilityScalarType.ABILITY_SCALAR_TYPE_TRIGGER => AbilityScalarValue.Trigger(),
            AbilityScalarType.ABILITY_SCALAR_TYPE_STRING => AbilityScalarValue.FromString(value.StringValue),
            AbilityScalarType.ABILITY_SCALAR_TYPE_UINT => AbilityScalarValue.FromUInt(value.UintValue),
            _ => FromUndeclaredScalar(value)
        };

    public static AbilityScalarType ToScalarType(AbilityScalarKind kind) => kind switch {
        AbilityScalarKind.Float => AbilityScalarType.ABILITY_SCALAR_TYPE_FLOAT,
        AbilityScalarKind.Int => AbilityScalarType.ABILITY_SCALAR_TYPE_INT,
        AbilityScalarKind.Bool => AbilityScalarType.ABILITY_SCALAR_TYPE_BOOL,
        AbilityScalarKind.Trigger => AbilityScalarType.ABILITY_SCALAR_TYPE_TRIGGER,
        AbilityScalarKind.String => AbilityScalarType.ABILITY_SCALAR_TYPE_STRING,
        AbilityScalarKind.UInt => AbilityScalarType.ABILITY_SCALAR_TYPE_UINT,
        _ => AbilityScalarType.ABILITY_SCALAR_TYPE_UNKNOWN
    };

    private static AbilityAppliedModifier ToAppliedModifier(AbilityModifierInstance modifier)
    {
        var applied = new AbilityAppliedModifier {
            ModifierLocalId = modifier.ModifierLocalId,
            ParentAbilityEntityId = modifier.ParentAbilityEntityId,
            ParentAbilityName = ToAbilityString(modifier.ParentAbilityName),
            InstancedAbilityId = modifier.InstancedAbilityId,
            InstancedModifierId = modifier.InstancedModifierId,
            ExistDuration = modifier.ExistDuration,
            ApplyEntityId = modifier.ApplyEntityId,
            IsAttachedParentAbility = modifier.IsAttachedParentAbility,
            SbuffUid = modifier.ServerBuffUid,
            IsServerbuffModifier = modifier.IsServerBuffModifier
        };

        if (modifier.ParentAbilityOverride != AbilityKey.Default)
            applied.ParentAbilityOverride = ToAbilityString(modifier.ParentAbilityOverride);

        if (modifier.HasAttachedModifier || modifier.AttachedModifierOwnerEntityId != 0 ||
            modifier.AttachedInstancedModifierId != 0 || modifier.AttachedNameHash != 0)
        {
            applied.AttachedInstancedModifier = new AbilityAttachedModifier {
                IsInvalid = modifier.AttachedModifierInvalid,
                OwnerEntityId = modifier.AttachedModifierOwnerEntityId,
                InstancedModifierId = modifier.AttachedInstancedModifierId,
                IsServerbuffModifier = modifier.AttachedIsServerBuffModifier,
                AttachNameHash = modifier.AttachedNameHash
            };
        }

        if (modifier.HasDurability || modifier.ReduceRatio != 0 ||
            modifier.RemainingDurability != 0 || modifier.IsDurabilityZero)
        {
            applied.ModifierDurability = new ModifierDurability {
                ReduceRatio = modifier.ReduceRatio,
                RemainingDurability = modifier.RemainingDurability
            };
        }

        return applied;
    }

    private static AbilityScalarValue FromUndeclaredScalar(AbilityScalarValueEntry value) =>
        value.ValueCase switch {
            AbilityScalarValueEntry.ValueOneofCase.FloatValue => AbilityScalarValue.FromFloat(value.FloatValue),
            AbilityScalarValueEntry.ValueOneofCase.IntValue => AbilityScalarValue.FromInt(value.IntValue),
            AbilityScalarValueEntry.ValueOneofCase.StringValue => AbilityScalarValue.FromString(value.StringValue),
            AbilityScalarValueEntry.ValueOneofCase.UintValue => AbilityScalarValue.FromUInt(value.UintValue),
            _ => AbilityScalarValue.Unknown()
        };

    private static IEnumerable<KeyValuePair<AbilityKey, AbilityScalarValue>> Order(
        IReadOnlyDictionary<AbilityKey, AbilityScalarValue> values
    ) =>
        values.OrderBy(pair => pair.Key.Hash)
            .ThenBy(pair => pair.Key.Name, StringComparer.Ordinal)
            .ThenBy(pair => pair.Key.IsHash);
}
