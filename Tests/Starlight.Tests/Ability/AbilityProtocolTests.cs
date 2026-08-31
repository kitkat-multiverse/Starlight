using System.Text;
using Starlight.Game.Ability;
using Starlight.Game.Resources;
using Starlight.Protocol;
using Xunit;

namespace Starlight.Tests.Ability;

public sealed class AbilityProtocolTests
{
    [Fact]
    public void ControlBlock_UsesPersistentEmbryoIdsAndHashes()
    {
        var component = Component();
        component.ResetEmbryos(["First", "Second", "Third"]);
        component.RemoveEmbryo(2);
        component.AddEmbryo("Fourth");

        var block = AbilityProtocol.ToControlBlock(component);

        Assert.Equal([1u, 3u, 4u], block.AbilityEmbryoList.Select(x => x.AbilityId));
        Assert.Equal(AbilityHash.Compute("First"), block.AbilityEmbryoList[0].AbilityNameHash);

        Assert.All(block.AbilityEmbryoList,
            x => Assert.Equal(AbilityHash.Compute("Default"), x.AbilityOverrideNameHash));
    }

    [Fact]
    public void SyncState_IsInitedOnlyAfterExplicitClientInitialization()
    {
        var component = Component();
        component.UpsertAbility(instancedAbilityId: 5, AbilityKey.FromName("Ability"));

        Assert.False(AbilityProtocol.ToSyncState(component).IsInited);

        component.MarkClientInitialized();

        Assert.True(AbilityProtocol.ToSyncState(component).IsInited);
    }

    [Fact]
    public void SyncState_PreservesSparseAbilitiesModifiersAndValueDomains()
    {
        var component = Component();
        var ability = component.UpsertAbility(instancedAbilityId: 8, AbilityKey.FromName("Ability"));
        ability.SetOverride(AbilityKey.FromHash(0x10203040), AbilityScalarValue.FromInt(-7));
        component.SetDynamicValue(AbilityKey.FromName("Local"), AbilityScalarValue.FromFloat(1.25f));
        component.SetServerGlobalValue(AbilityKey.FromName("SGV_Global"), AbilityScalarValue.FromUInt(9));
        component.MarkClientInitialized();

        component.UpsertModifier(new AbilityModifierInstance(
            instancedModifierId: 14,
            instancedAbilityId: 8,
            modifierLocalId: 3,
            component.Owner.EntityId,
            AbilityKey.FromName("Ability"),
            AbilityKey.Default) {
            ApplyEntityId = 0x02000002,
            ReduceRatio = 0.2f,
            RemainingDurability = 0.8f,
            AttachedModifierOwnerEntityId = component.Owner.EntityId,
            AttachedInstancedModifierId = 13
        });

        var sync = AbilityProtocol.ToSyncState(component);

        var applied = Assert.Single(sync.AppliedAbilities);
        Assert.Equal(expected: 8u, applied.InstancedAbilityId);
        Assert.Equal(AbilityHash.Compute("Ability"), applied.AbilityName!.Hash);
        Assert.Equal(expected: -7, Assert.Single(applied.OverrideMap).IntValue);

        var modifier = Assert.Single(sync.AppliedModifiers);
        Assert.Equal(expected: 14u, modifier.InstancedModifierId);
        Assert.Equal(expected: 8u, modifier.InstancedAbilityId);
        Assert.Equal(expected: 0x02000002u, modifier.ApplyEntityId);
        Assert.Equal(expected: 0.2f, modifier.ModifierDurability!.ReduceRatio);
        Assert.Equal(expected: 0.8f, modifier.ModifierDurability.RemainingDurability);

        Assert.Single(sync.DynamicValueMap);
        Assert.Single(sync.SgvDynamicValueMap);
        Assert.Equal(AbilityScalarType.ABILITY_SCALAR_TYPE_FLOAT, sync.DynamicValueMap[0].ValueType);
        Assert.Equal(AbilityScalarType.ABILITY_SCALAR_TYPE_UINT, sync.SgvDynamicValueMap[0].ValueType);
    }

    [Fact]
    public void ScalarRoundTrip_PreservesOneofAndDeclaredType()
    {
        var values = new[] {
            AbilityScalarValue.FromFloat(2.5f),
            AbilityScalarValue.FromInt(-2),
            AbilityScalarValue.FromBool(true),
            AbilityScalarValue.Trigger(),
            AbilityScalarValue.FromString("value"),
            AbilityScalarValue.FromUInt(42)
        };

        foreach (var value in values)
        {
            var proto = AbilityProtocol.ToScalarEntry(AbilityKey.FromName("Key"), value);
            Assert.Equal(value, AbilityProtocol.FromScalarEntry(proto));
        }
    }

    [Fact]
    public void SyncState_OmitsStaticServerAbilitiesWithoutOverridesForNonTargetEntities()
    {
        var component = new AbilityComponent(new AbilityOwner(EntityId: 0x02000001, AbilityOwnerType.Monster));
        var definition = new Starlight.Game.Resources.Binary.AbilityConfig { AbilityName = "Static" };
        component.AddServerAbility("Static", "Default", definition);

        var sync = AbilityProtocol.ToSyncState(component);

        Assert.Empty(sync.AppliedAbilities);
    }

    [Fact]
    public void SyncState_EmitsNonTargetServerAbilityWhenItHasOverrides()
    {
        var component = new AbilityComponent(new AbilityOwner(EntityId: 0x02000001, AbilityOwnerType.Monster));
        var definition = new Starlight.Game.Resources.Binary.AbilityConfig { AbilityName = "Static" };
        var ability = component.AddServerAbility("Static", "Default", definition)!;
        ability.SetOverride(AbilityKey.FromName("Value"), AbilityScalarValue.FromFloat(1f));

        var applied = Assert.Single(AbilityProtocol.ToSyncState(component).AppliedAbilities);

        Assert.Equal(AbilityHash.Compute("Static"), applied.AbilityName!.Hash);
        Assert.Null(applied.AbilityOverride);
    }

    [Fact]
    public void DefaultAbilityString_IsEmpty()
    {
        var value = AbilityProtocol.ToAbilityString(AbilityKey.Default);
        Assert.Equal(AbilityString.TypeOneofCase.None, value.TypeCase);
    }

    private static AbilityComponent Component() =>
        new(new AbilityOwner(EntityId: 0x01000001, AbilityOwnerType.Avatar));

    private sealed class MemoryResourceLoader(IReadOnlyDictionary<string, string> files) : IResourceLoader
    {
        public string[] ListFiles(string path, string searchPattern = "*", bool recursive = false) =>
            files.Keys.Where(file => file.StartsWith(path, StringComparison.Ordinal)).ToArray();

        public byte[] ReadRaw(string path) => Encoding.UTF8.GetBytes(files[path]);
    }
}
