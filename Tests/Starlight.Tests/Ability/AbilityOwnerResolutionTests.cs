using System.Text.Json;
using Starlight.Game.Ability;
using Starlight.Game.Ability.Handlers;
using Starlight.Game.Ability.Handlers.Actions;
using Starlight.Game.Resources.Binary;
using Starlight.Protocol;
using Xunit;

namespace Starlight.Tests.Ability;

public sealed class AbilityOwnerResolutionTests
{
    [Fact]
    public void AbilityOwner_CarriesEntityOwnership()
    {
        var owner = new AbilityOwner(
            EntityId: 30,
            AbilityOwnerType.Gadget,
            OwnerEntityId: 20,
            PropOwnerEntityId: 10);

        Assert.Equal(expected: 20u, owner.OwnerEntityId);
        Assert.Equal(expected: 10u, owner.PropOwnerEntityId);
    }

    [Fact]
    public async Task CopyGlobalValue_OriginOwnerWalksNestedGadgetOwnership()
    {
        var scope = new AbilityScope();
        var furina = scope.Register(new AbilityOwner(EntityId: 10, AbilityOwnerType.Avatar, PlayerUid: 1));
        var switchedAvatar = scope.Register(new AbilityOwner(EntityId: 11, AbilityOwnerType.Avatar, PlayerUid: 1));

        var controller = scope.Register(new AbilityOwner(
            EntityId: 20,
            AbilityOwnerType.Gadget,
            PlayerUid: 1,
            OwnerEntityId: furina.Owner.EntityId,
            PropOwnerEntityId: furina.Owner.EntityId));

        var statue = scope.Register(new AbilityOwner(
            EntityId: 30,
            AbilityOwnerType.Gadget,
            PlayerUid: 1,
            OwnerEntityId: controller.Owner.EntityId,
            PropOwnerEntityId: controller.Owner.EntityId));

        furina.SetDynamicValue(AbilityKey.FromName("SourceValue"), AbilityScalarValue.FromFloat(42f));
        switchedAvatar.SetDynamicValue(AbilityKey.FromName("SourceValue"), AbilityScalarValue.FromFloat(7f));

        var context = Context(
            scope,
            statue,
            switchedAvatar,
            switchedAvatar.Owner.EntityId,
            Node("CopyGlobalValue", json: """
                                          { "srcTarget": "OriginOwner", "dstTarget": "Self", "srcKey": "SourceValue", "dstKey": "CopiedValue" }
                                          """));

        await new CopyGlobalValueHandler().HandleAsync(context);

        Assert.True(statue.DynamicValues.TryGetValue(AbilityKey.FromName("CopiedValue"), out var copied));
        Assert.Equal(expected: 42f, copied.FloatValue);
    }

    [Fact]
    public async Task CopyGlobalValue_CurLocalAvatarIsIndependentFromOriginOwner()
    {
        var scope = new AbilityScope();
        var furina = scope.Register(new AbilityOwner(EntityId: 10, AbilityOwnerType.Avatar, PlayerUid: 1));
        var switchedAvatar = scope.Register(new AbilityOwner(EntityId: 11, AbilityOwnerType.Avatar, PlayerUid: 1));

        var gadget = scope.Register(new AbilityOwner(
            EntityId: 20,
            AbilityOwnerType.Gadget,
            PlayerUid: 1,
            OwnerEntityId: furina.Owner.EntityId,
            PropOwnerEntityId: furina.Owner.EntityId));

        furina.SetDynamicValue(AbilityKey.FromName("SourceValue"), AbilityScalarValue.FromFloat(42f));
        switchedAvatar.SetDynamicValue(AbilityKey.FromName("SourceValue"), AbilityScalarValue.FromFloat(7f));

        var context = Context(
            scope,
            gadget,
            furina,
            switchedAvatar.Owner.EntityId,
            Node("CopyGlobalValue", json: """
                                          { "srcTarget": "CurLocalAvatar", "dstTarget": "Self", "srcKey": "SourceValue", "dstKey": "CopiedValue" }
                                          """));

        await new CopyGlobalValueHandler().HandleAsync(context);

        Assert.True(gadget.DynamicValues.TryGetValue(AbilityKey.FromName("CopiedValue"), out var copied));
        Assert.Equal(expected: 7f, copied.FloatValue);
    }

    [Fact]
    public async Task Dispatch_ResolvesInstancedAbilityFromInvokeEntity_NotTargetEntity()
    {
        var scope = new AbilityScope();
        var source = scope.Register(new AbilityOwner(EntityId: 10, AbilityOwnerType.Avatar, PlayerUid: 1));
        var target = scope.Register(new AbilityOwner(EntityId: 20, AbilityOwnerType.Monster));

        var sourceAbility = source.UpsertAbility(
            instancedAbilityId: 5,
            AbilityKey.FromName("SourceAbility"),
            definition: new AbilityConfig { AbilityName = "SourceAbility" });

        target.UpsertAbility(
            instancedAbilityId: 5,
            AbilityKey.FromName("TargetAbility"),
            definition: new AbilityConfig { AbilityName = "TargetAbility" });

        AbilityContext? captured = null;

        var handlers = new AbilityInvokeHandlerRegistry([
            new CaptureArgumentHandler(context => captured = context)
        ]);

        var module = new AbilityModule(
            null!,
            null!,
            null!,
            null!,
            new AbilityRuntimeConfig(static () => false),
            handlers,
            null!,
            null!);

        await module.Dispatch(
            new AbilityScopeContext(scope, PeerId: 1, HostPeerId: 1, SceneId: 3),
            new AbilityInvokeEntry {
                EntityId = source.Owner.EntityId,
                ArgumentType = AbilityInvokeArgument.ABILITY_META_GLOBAL_FLOAT_VALUE,
                Head = new AbilityInvokeEntryHead {
                    InstancedAbilityId = 5,
                    TargetId = target.Owner.EntityId
                }
            });

        Assert.NotNull(captured);
        Assert.Same(sourceAbility, captured.Ability);
        Assert.Same(source, captured.Source);
        Assert.Same(target, captured.Target);
    }

    [Fact]
    public void OriginOwnerResolution_DetectsOwnershipCycles()
    {
        var scope = new AbilityScope();
        var first = scope.Register(new AbilityOwner(EntityId: 10, AbilityOwnerType.Gadget, OwnerEntityId: 20));
        scope.Register(new AbilityOwner(EntityId: 20, AbilityOwnerType.Gadget, OwnerEntityId: 10));
        var world = new AbilityScopeContext(scope, PeerId: 1, HostPeerId: 1, SceneId: 3);

        Assert.False(world.TryGetOriginOwner(first, out _));
    }

    private static AbilityContext Context(
        AbilityScope scope,
        AbilityComponent source,
        AbilityComponent target,
        uint currentAvatarEntityId,
        AbilityConfigNode action
    ) =>
        new(
            null!,
            new AbilityScopeContext(
                scope,
                PeerId: 1,
                HostPeerId: 1,
                SceneId: 3,
                currentAvatarEntityId),
            new AbilityRuntimeConfig(static () => false),
            new AbilityInvokeEntry { EntityId = source.Owner.EntityId },
            source,
            target,
            Ability: null,
            Modifier: null,
            Definition: null,
            action,
            Mixin: null);

    private static AbilityConfigNode Node(string type, string json)
    {
        using var document = JsonDocument.Parse(json);

        return new AbilityConfigNode {
            Type = type,
            Values = document.RootElement.EnumerateObject().ToDictionary(pair => pair.Name, pair => pair.Value.Clone())
        };
    }

    private sealed class CaptureArgumentHandler(Action<AbilityContext> capture)
        : AbilityArgumentHandler(AbilityInvokeArgument.ABILITY_META_GLOBAL_FLOAT_VALUE)
    {
        public override ValueTask HandleAsync(AbilityContext context)
        {
            capture(context);
            return ValueTask.CompletedTask;
        }
    }
}
