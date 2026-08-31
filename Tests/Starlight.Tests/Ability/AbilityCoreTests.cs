using Starlight.Game.Ability;
using Xunit;

namespace Starlight.Tests.Ability;

public sealed class AbilityCoreTests
{
    [Fact]
    public void Hash_UsesClient131HashWithUIntOverflow()
    {
        Assert.Equal(expected: 0x463810D9u, AbilityHash.Compute("Default"));

        Assert.Equal(AbilityHash.Compute("Avatar_DefaultAbility_VisionReplaceDieInvincible"),
            AbilityKey.FromName("Avatar_DefaultAbility_VisionReplaceDieInvincible").Hash);
    }

    [Fact]
    public void ResetEmbryos_PreservesDeclarationOrderAndStartsAtOne()
    {
        var component = Component();

        component.ResetEmbryos(["A", "C", "B"]);

        Assert.Collection(component.Embryos,
            x => {
                Assert.Equal(expected: 1u, x.AbilityId);
                Assert.Equal("A", x.Name.Name);
            },
            x => {
                Assert.Equal(expected: 2u, x.AbilityId);
                Assert.Equal("C", x.Name.Name);
            },
            x => {
                Assert.Equal(expected: 3u, x.AbilityId);
                Assert.Equal("B", x.Name.Name);
            });
    }

    [Fact]
    public void RemoveThenAddEmbryo_DoesNotRenumberOrReuseIdentity()
    {
        var component = Component();
        component.ResetEmbryos(["A", "B", "C"]);

        Assert.True(component.RemoveEmbryo(2));
        var added = component.AddEmbryo("D");

        Assert.Equal(expected: 4u, added.AbilityId);
        Assert.Equal([1u, 3u, 4u], component.Embryos.Select(x => x.AbilityId));
    }

    [Fact]
    public void AppliedAbilities_AreSparseAndOrderedByInstanceId()
    {
        var component = Component();

        component.UpsertAbility(instancedAbilityId: 7, AbilityKey.FromName("Seven"));
        component.UpsertAbility(instancedAbilityId: 2, AbilityKey.FromName("Two"));

        Assert.Equal([2u, 7u], component.AppliedAbilities.Keys);
        Assert.False(component.AppliedAbilities.ContainsKey(1));
        Assert.False(component.AppliedAbilities.ContainsKey(3));
    }

    [Fact]
    public void OverrideMap_SetClearAndReinitializeAreLossless()
    {
        var component = Component();
        var ability = component.UpsertAbility(instancedAbilityId: 3, AbilityKey.FromName("Ability"));
        var first = AbilityKey.FromName("First");
        var second = AbilityKey.FromHash(0x12345678);

        ability.SetOverride(first, AbilityScalarValue.FromFloat(2.5f));
        ability.SetOverride(second, AbilityScalarValue.FromUInt(8));
        Assert.True(ability.ClearOverride(first));

        ability.ReinitializeOverrides([
            new KeyValuePair<AbilityKey, AbilityScalarValue>(first, AbilityScalarValue.FromInt(-4))
        ]);

        Assert.Single(ability.Overrides);
        Assert.Equal(expected: -4, ability.Overrides[first].IntValue);
    }

    [Fact]
    public void Modifiers_AreSparseAndRetainApplyEntityAndDurability()
    {
        var component = Component();

        var modifier = new AbilityModifierInstance(
            instancedModifierId: 11,
            instancedAbilityId: 4,
            modifierLocalId: 2,
            component.Owner.EntityId,
            AbilityKey.FromName("Ability"),
            AbilityKey.Default);

        modifier.ApplyEntityId = 0x01020304;
        modifier.ReduceRatio = 0.25f;
        modifier.RemainingDurability = 0.75f;
        component.UpsertModifier(modifier);

        Assert.Same(modifier, component.AppliedModifiers[11]);
        Assert.Equal(expected: 0x01020304u, component.AppliedModifiers[11].ApplyEntityId);
        Assert.Equal(expected: 0.25f, component.AppliedModifiers[11].ReduceRatio);
        Assert.Equal(expected: 0.75f, component.AppliedModifiers[11].RemainingDurability);
        Assert.True(component.RemoveModifier(11));
        Assert.Empty(component.AppliedModifiers);
    }

    [Fact]
    public void DynamicAndServerGlobalValuesRemainSeparate()
    {
        var component = Component();
        var dynamicKey = AbilityKey.FromName("LocalValue");
        var sgvKey = AbilityKey.FromName("SGV_GlobalValue");

        component.SetDynamicValue(dynamicKey, AbilityScalarValue.FromFloat(1.5f));
        component.SetServerGlobalValue(sgvKey, AbilityScalarValue.FromUInt(3));

        Assert.Equal(expected: 1.5f, component.DynamicValues[dynamicKey].FloatValue);
        Assert.Equal(expected: 3u, component.ServerGlobalValues[sgvKey].UIntValue);
        Assert.False(component.DynamicValues.ContainsKey(sgvKey));
        Assert.False(component.ServerGlobalValues.ContainsKey(dynamicKey));
    }

    [Fact]
    public void ClientInitialization_IsExplicitAndResettable()
    {
        var component = Component();
        component.UpsertAbility(instancedAbilityId: 1, AbilityKey.FromName("Ability"));

        Assert.False(component.IsClientInitialized);
        component.MarkClientInitialized();
        Assert.True(component.IsClientInitialized);
        component.ResetClientState();
        Assert.False(component.IsClientInitialized);
        Assert.Empty(component.AppliedAbilities);
        Assert.Empty(component.AppliedModifiers);
    }

    [Fact]
    public void Scope_StoresOneSharedComponentPerEntityAndSupportsAllOwnerKinds()
    {
        var scope = new AbilityScope();

        var owners = Enum.GetValues<AbilityOwnerType>()
            .Where(type => type != AbilityOwnerType.Unknown)
            .Select((type, i) => new AbilityOwner((uint)i + 1, type))
            .ToArray();

        foreach (var owner in owners)
        {
            scope.Register(owner, [owner.Type.ToString()]);
        }

        foreach (var owner in owners)
        {
            Assert.True(scope.TryGet(owner.EntityId, out var component));
            Assert.Equal(owner, component.Owner);
        }
    }

    [Fact]
    public void ResetClientState_PreservesServerInstancedAbilities()
    {
        var component = new AbilityComponent(new AbilityOwner(EntityId: 0x02000001, AbilityOwnerType.Monster));
        var definition = new Game.Resources.Binary.AbilityConfig { AbilityName = "ServerAbility" };
        component.AddServerAbility("ServerAbility", "Default", definition);
        component.UpsertAbility(instancedAbilityId: 50, AbilityKey.FromName("ClientAbility"));
        component.MarkClientInitialized();

        component.ResetClientState();

        Assert.False(component.IsClientInitialized);
        var server = Assert.Single(component.AppliedAbilities.Values);
        Assert.Equal("ServerAbility", server.Name.Name);
        Assert.Equal(AbilityInstanceOrigin.Server, server.Origin);
    }

    [Fact]
    public void TargetAbilitySpecials_AccumulateDeltaAndRatio()
    {
        var component = Component();
        var ability = AbilityKey.FromName("Ability");
        var param = AbilityKey.FromName("Param");

        component.AddTargetAbilitySpecial(ability, param, delta: 2f, ratio: 0.25f);
        component.AddTargetAbilitySpecial(ability, param, delta: -1f, ratio: 0.5f);

        Assert.True(component.TryApplyTargetAbilitySpecial(ability, param, value: 10f, out var result));
        Assert.Equal((10f + 1f) * 1.75f, result);
    }

    private static AbilityComponent Component() =>
        new(new AbilityOwner(EntityId: 0x01000001, AbilityOwnerType.Avatar));
}
