using Microsoft.Extensions.DependencyInjection;
using Starlight.Game.Ability;
using Starlight.Game.Ability.Handlers;
using Starlight.Game.Resources.Binary;
using Starlight.Protocol;
using Xunit;

namespace Starlight.Tests.Ability;

public sealed class AbilityInvokeHandlerRegistryTests
{
    [Fact]
    public void AbilityActionHandler_InfersTypeFromHandlerClassName()
    {
        var handler = new HealHPHandler();

        Assert.Equal("HealHP", handler.ActionType);
    }

    [Fact]
    public void AbilityMixinHandler_InfersTypeFromHandlerClassName()
    {
        var handler = new AvatarLevitateMixinHandler();

        Assert.Equal("AvatarLevitateMixin", handler.MixinType);
    }

    [Fact]
    public void AddAbilityInvokeHandlers_DiscoversHandlersWithoutPerHandlerRegistration()
    {
        var services = new ServiceCollection();

        services.AddAbilityInvokeHandlers(typeof(AutoDiscoveredActionHandler).Assembly);

        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IAbilityInvokeHandler) &&
            descriptor.ImplementationType == typeof(AutoDiscoveredActionHandler));

        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(AbilityInvokeHandlerRegistry));
    }

    [Fact]
    public void AddAbilityInvokeHandlers_IsIdempotentForTheSameAssembly()
    {
        var services = new ServiceCollection();
        var assembly = typeof(AutoDiscoveredActionHandler).Assembly;

        services.AddAbilityInvokeHandlers(assembly);
        services.AddAbilityInvokeHandlers(assembly);

        Assert.Single(services.Where(descriptor =>
            descriptor.ServiceType == typeof(IAbilityInvokeHandler) &&
            descriptor.ImplementationType == typeof(AutoDiscoveredActionHandler)));

        Assert.Single(services.Where(descriptor =>
            descriptor.ServiceType == typeof(AbilityInvokeHandlerRegistry)));
    }

    [Fact]
    public async Task DispatchAsync_RoutesArgumentActionAndMixinHandlers()
    {
        var calls = new List<string>();

        var registry = new AbilityInvokeHandlerRegistry([
            new RecordingArgumentHandler(AbilityInvokeArgument.ABILITY_ACTION_TRIGGER_ABILITY, calls, "argument"),
            new RecordingActionHandler("SetOverrideMapValue", calls, "action"),
            new RecordingMixinHandler("AvatarLevitateMixin", calls, "mixin")
        ]);

        await registry.DispatchAsync(Context(
            AbilityInvokeArgument.ABILITY_ACTION_TRIGGER_ABILITY,
            new AbilityConfigNode { Type = "SetOverrideMapValue" },
            new AbilityConfigNode { Type = "AvatarLevitateMixin" }));

        Assert.Equal(["argument", "action", "mixin"], calls);
    }

    [Fact]
    public async Task DispatchAsync_UsesOrdinalNodeTypeMatching()
    {
        var calls = new List<string>();

        var registry = new AbilityInvokeHandlerRegistry([
            new RecordingActionHandler("SetOverrideMapValue", calls, "action")
        ]);

        await registry.DispatchAsync(Context(
            AbilityInvokeArgument.ABILITY_ACTION_TRIGGER_ABILITY,
            new AbilityConfigNode { Type = "setoverridemapvalue" }));

        Assert.Empty(calls);
    }

    [Fact]
    public async Task DispatchAsync_OrdersHandlersByOrderThenRegistrationOrder()
    {
        var calls = new List<string>();

        var registry = new AbilityInvokeHandlerRegistry([
            new RecordingActionHandler("SetOverrideMapValue", calls, "late-a", order: 20),
            new RecordingActionHandler("SetOverrideMapValue", calls, "early", order: -10),
            new RecordingActionHandler("SetOverrideMapValue", calls, "late-b", order: 20)
        ]);

        await registry.DispatchAsync(Context(
            AbilityInvokeArgument.ABILITY_ACTION_TRIGGER_ABILITY,
            new AbilityConfigNode { Type = "SetOverrideMapValue" }));

        Assert.Equal(["early", "late-a", "late-b"], calls);
    }

    [Fact]
    public async Task DispatchAsync_IgnoresUnregisteredKinds()
    {
        var calls = new List<string>();

        var registry = new AbilityInvokeHandlerRegistry([
            new RecordingActionHandler("OtherAction", calls, "other")
        ]);

        await registry.DispatchAsync(Context(
            AbilityInvokeArgument.ABILITY_ACTION_TRIGGER_ABILITY,
            new AbilityConfigNode { Type = "SetOverrideMapValue" }));

        Assert.Empty(calls);
    }

    private static AbilityContext Context(
        AbilityInvokeArgument argument,
        AbilityConfigNode? action = null,
        AbilityConfigNode? mixin = null
    )
    {
        var scope = new AbilityScope();
        var source = scope.Register(new AbilityOwner(EntityId: 0x01000001, AbilityOwnerType.Avatar));
        var invoke = new AbilityInvokeEntry { ArgumentType = argument, EntityId = source.Owner.EntityId };

        return new AbilityContext(
            null!,
            new AbilityScopeContext(scope, PeerId: 1, HostPeerId: 1, SceneId: 3),
            new AbilityRuntimeConfig(static () => false),
            invoke,
            source,
            source,
            Ability: null,
            Modifier: null,
            Definition: null,
            action,
            mixin);
    }

    private sealed class HealHPHandler : AbilityActionHandler
    {
        public override ValueTask HandleAsync(AbilityContext context) => ValueTask.CompletedTask;
    }

    private sealed class AvatarLevitateMixinHandler : AbilityMixinHandler
    {
        public override ValueTask HandleAsync(AbilityContext context) => ValueTask.CompletedTask;
    }

    public sealed class AutoDiscoveredActionHandler : AbilityActionHandler
    {
        public override ValueTask HandleAsync(AbilityContext context) => ValueTask.CompletedTask;
    }

    private sealed class RecordingActionHandler(
        string type,
        List<string> calls,
        string value,
        int order = 0
    ) : AbilityActionHandler(type)
    {
        public override int Order => order;

        public override ValueTask HandleAsync(AbilityContext context)
        {
            calls.Add(value);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingMixinHandler(
        string type,
        List<string> calls,
        string value,
        int order = 0
    ) : AbilityMixinHandler(type)
    {
        public override int Order => order;

        public override ValueTask HandleAsync(AbilityContext context)
        {
            calls.Add(value);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingArgumentHandler(
        AbilityInvokeArgument argument,
        List<string> calls,
        string value,
        int order = 0
    ) : AbilityArgumentHandler(argument)
    {
        public override int Order => order;

        public override ValueTask HandleAsync(AbilityContext context)
        {
            calls.Add(value);
            return ValueTask.CompletedTask;
        }
    }
}
