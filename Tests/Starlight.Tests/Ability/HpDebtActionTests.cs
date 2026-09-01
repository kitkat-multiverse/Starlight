using Google.Protobuf;
using Microsoft.Extensions.DependencyInjection;
using Starlight.Game.Ability;
using Starlight.Game.Ability.Handlers.Actions;
using Starlight.Game.Ability.Handlers.Mixins;
using Starlight.Game.Ability.HpDebts;
using Starlight.Game.Modules;
using Starlight.Game.Player;
using Starlight.Game.Resources.Binary;
using Starlight.Protobuf.Registry;
using Starlight.Protocol;
using Starlight.Protocol.V70;
using Starlight.Rpc.Tunnel;
using System.Text.Json;
using Xunit;

namespace Starlight.Tests.Ability;

public sealed class HpDebtActionTests
{
    private const uint CurHp = 1010;
    private const uint MaxHp = 2000;
    private const uint CurHpDebts = 2004;
    private const uint CurHpPaidDebts = 2005;

    [Fact]
    public async Task AddHpDebts_MutatesDebtAndBroadcastsGrasscutterReasonPacket()
    {
        var (service, forwarder) = Runtime();
        var source = Avatar();
        source.SetFightProperty(MaxHp, value: 1000f);
        source.SetFightProperty(CurHpDebts, value: 100f);

        var context = Context(source, action: Node("AddHPDebts", json: """
                                                                       { "value": 250, "hpDebtTag": "test" }
                                                                       """));
        await new AddHPDebtsHandler(service).HandleAsync(context);

        Assert.Equal(expected: 350f, source.GetFightProperty(CurHpDebts));
        var update = Assert.IsType<EntityFightPropUpdateNotify>(forwarder.Messages[0]);
        Assert.Equal(expected: 350f, update.FightPropMap[CurHpDebts]);

        var reason = Assert.IsType<EntityFightPropChangeReasonNotify>(forwarder.Messages[1]);
        Assert.Equal(expected: 250f, reason.PropDelta);
        Assert.Equal(PropChangeReason.PROP_CHANGE_REASON_ABILITY, reason.Reason);
        Assert.Equal(ChangeHpDebts.CHANGE_HP_DEBTS_ADD_ABILITY, reason.ChangeHpDebts);
        Assert.Equal(expected: 0f, reason.PaidHpDebts);
    }

    [Fact]
    public async Task ReduceHpDebts_ClampsAtZeroAndReportsPaidDebt()
    {
        var (service, forwarder) = Runtime();
        var source = Avatar();
        source.SetFightProperty(MaxHp, value: 1000f);
        source.SetFightProperty(CurHpDebts, value: 100f);

        await new ReduceHPDebtsHandler(service).HandleAsync(
            Context(source, action: Node("ReduceHPDebts", json: """{ "value": 250 }""")));

        Assert.Equal(expected: 0f, source.GetFightProperty(CurHpDebts));
        var reason = Assert.IsType<EntityFightPropChangeReasonNotify>(forwarder.Messages[1]);
        Assert.Equal(expected: -100f, reason.PropDelta);
        Assert.Equal(expected: 100f, reason.PaidHpDebts);
        Assert.Equal(ChangeHpReason.CHANGE_HP_REASON_ADD_ABILITY, reason.ChangeHpReason);
        Assert.Equal(ChangeHpDebts.CHANGE_HP_DEBTS_PAY_FINISH, reason.ChangeHpDebts);
    }

    [Fact]
    public async Task LimitHpDebtsByTag_SnapshotsAbsoluteCap_AndRemovalResetsIt()
    {
        var (service, _) = Runtime();
        var source = Avatar();
        source.SetFightProperty(MaxHp, value: 1000f);
        source.SetFightProperty(CurHpDebts, value: 200f);

        var limitMixin = Node("LimitHpDebtsByTagMixin", json: """
                                                              { "maxHpDebtRatio": 0.5, "hpDebtTags": ["limited"] }
                                                              """);

        var definition = new AbilityConfig {
            Modifiers = {
                ["Limiter"] = new AbilityModifierConfig { ModifierMixins = { limitMixin } }
            }
        };
        var ability = source.UpsertAbility(instancedAbilityId: 1, AbilityKey.FromName("Test"), definition: definition);

        var modifier = new AbilityModifierInstance(instancedModifierId: 1, instancedAbilityId: 1, modifierLocalId: 0, source.Owner.EntityId,
            ability.Name, ability.Override) {
            ModifierName = "Limiter"
        };
        source.UpsertModifier(modifier);

        var mixinContext = Context(source, ability, modifier, definition, mixin: limitMixin);
        await new LimitHpDebtsByTagMixinHandler(service).HandleAsync(mixinContext);

        await service.ChangeAsync(mixinContext, source, amount: 1000f, "limited");
        Assert.Equal(expected: 700f, source.GetFightProperty(CurHpDebts)); // 200 + 50% max HP snapshot

        source.RemoveModifier(modifier.InstancedModifierId);
        await service.ChangeAsync(mixinContext, source, amount: 1000f, "limited");
        Assert.Equal(expected: 1700f, source.GetFightProperty(CurHpDebts));
    }

    [Fact]
    public async Task SwitchHealToHpDebts_ConvertsHealAndSuppressesNormalHealing()
    {
        var (service, forwarder) = Runtime();
        var processor = new SwitchHealToHpDebtsProcessor(service);
        var source = Avatar();
        source.SetFightProperty(MaxHp, value: 1000f);
        source.SetFightProperty(CurHp, value: 500f);
        source.SetFightProperty(CurHpDebts, value: 0f);

        var switchMixin = Node("SwitchHealToHPDebtsMixin", json: """
                                                                 {
                                                                   "ratio": 2,
                                                                   "predicates": [
                                                                     { "$type": "ByNot", "predicates": [
                                                                         { "$type": "ONJEHDDFFCH", "healTags": ["ignored"] }
                                                                     ] }
                                                                   ]
                                                                 }
                                                                 """);

        var definition = new AbilityConfig {
            Modifiers = {
                ["Switch"] = new AbilityModifierConfig { ModifierMixins = { switchMixin } }
            }
        };
        var ability = source.UpsertAbility(instancedAbilityId: 1, AbilityKey.FromName("Test"), definition: definition);

        source.UpsertModifier(new AbilityModifierInstance(instancedModifierId: 1, instancedAbilityId: 1, modifierLocalId: 0,
            source.Owner.EntityId, ability.Name, ability.Override) {
            ModifierName = "Switch"
        });

        var healAction = Node("HealHP", json: """{ "amount": 100, "healTag": "normal" }""");

        await new HealHPHandler(service, processor).HandleAsync(
            Context(source, ability, definition: definition, action: healAction));

        Assert.Equal(expected: 500f, source.GetFightProperty(CurHp));
        Assert.Equal(expected: 200f, source.GetFightProperty(CurHpDebts));
        Assert.DoesNotContain(forwarder.Messages, message => message is CombatInvocationsNotify);
    }

    [Fact]
    public async Task NormalHeal_RepaysDebtInGrasscutterPacketOrder_AndSendsHealTag()
    {
        var (service, forwarder, protocol) = RuntimeWithProtocol();
        var processor = new SwitchHealToHpDebtsProcessor(service);
        var source = Avatar();
        source.SetFightProperty(MaxHp, value: 1000f);
        source.SetFightProperty(CurHp, value: 500f);
        source.SetFightProperty(CurHpDebts, value: 40f);

        var definition = new AbilityConfig();
        var ability = source.UpsertAbility(instancedAbilityId: 1, AbilityKey.FromName("Heal"), definition: definition);

        await new HealHPHandler(service, processor).HandleAsync(
            Context(source, ability, definition: definition,
                action: Node("HealHP", json: """{ "amount": 100, "healTag": "test_heal" }""")));

        Assert.Equal(expected: 560f, source.GetFightProperty(CurHp));
        Assert.Equal(expected: 0f, source.GetFightProperty(CurHpDebts));
        Assert.Equal(expected: 0f, source.GetFightProperty(CurHpPaidDebts));

        Assert.IsType<EntityFightPropUpdateNotify>(forwarder.Messages[0]); // debt
        Assert.IsType<EntityFightPropUpdateNotify>(forwarder.Messages[1]); // paid debt = 40
        var debtReason = Assert.IsType<EntityFightPropChangeReasonNotify>(forwarder.Messages[2]);
        Assert.Equal(ChangeHpDebts.CHANGE_HP_DEBTS_PAY_FINISH, debtReason.ChangeHpDebts);
        Assert.IsType<EntityFightPropUpdateNotify>(forwarder.Messages[3]); // paid debt = 0
        Assert.IsType<EntityFightPropUpdateNotify>(forwarder.Messages[4]); // hp

        var combat = Assert.IsType<CombatInvocationsNotify>(forwarder.Messages[5]);
        var evt = new EvtBeingHealedNotify();

        using (var input = combat.InvokeList[0].CombatData.CreateCodedInput())
        {
            protocol.Deserialize(evt, input);
        }
        Assert.Equal(expected: 100f, evt.HealAmount);
        //Assert.Equal("test_heal", evt.HealTag);

        Assert.IsType<EntityFightPropChangeReasonNotify>(forwarder.Messages[6]); // avatar HP reason
    }

    [Fact]
    public void V70_ProtocolRoundTripsHpDebtReasonPaidDebtAndHealTag()
    {
        var protocol = new V70ProtocolRegistry();

        var reason = RoundTrip(protocol, new EntityFightPropChangeReasonNotify {
            EntityId = 1,
            PropType = CurHpDebts,
            PropDelta = -25f,
            PaidHpDebts = 25f,
            ChangeHpDebts = ChangeHpDebts.CHANGE_HP_DEBTS_REDUCE_ABILITY
        });
        Assert.Equal(expected: 25f, reason.PaidHpDebts);
        Assert.Equal(ChangeHpDebts.CHANGE_HP_DEBTS_REDUCE_ABILITY, reason.ChangeHpDebts);

        var healed = RoundTrip(protocol, new EvtBeingHealedNotify {
            TargetId = 1,
            SourceId = 1,
            HealAmount = 10f,
            HealTag = "tag"
        });
        Assert.Equal("tag", healed.HealTag);
    }

    private static T RoundTrip<T>(ProtocolRegistry protocol, T message)
        where T : class, Starlight.Protobuf.Core.IMessage, new()
    {
        var data = protocol.Serialize(message);
        var result = new T();
        using var input = new CodedInputStream(data);
        protocol.Deserialize(result, input);
        return result;
    }

    private static (HpDebtService Service, RecordingForwarder Forwarder) Runtime()
    {
        var (service, forwarder, _) = RuntimeWithProtocol();
        return (service, forwarder);
    }

    private static (HpDebtService Service, RecordingForwarder Forwarder, ProtocolRegistry Protocol) RuntimeWithProtocol()
    {
        var forwarder = new RecordingForwarder();
        var protocol = new V70ProtocolRegistry();
        return (new HpDebtService(forwarder, protocol), forwarder, protocol);
    }

    private static AbilityComponent Avatar() =>
        new(new AbilityOwner(EntityId: 0x01000001, AbilityOwnerType.Avatar));

    private static AbilityContext Context(
        AbilityComponent component,
        AbilityInstance? ability = null,
        AbilityModifierInstance? modifier = null,
        AbilityConfig? definition = null,
        AbilityConfigNode? action = null,
        AbilityConfigNode? mixin = null
    )
    {
        var scope = new AbilityScope();
        scope.Register(component.Owner);

        return new AbilityContext(
            Player(),
            new AbilityScopeContext(scope, PeerId: 1, HostPeerId: 1, SceneId: 3),
            new AbilityRuntimeConfig(static () => false),
            new AbilityInvokeEntry { EntityId = component.Owner.EntityId },
            component,
            component,
            ability,
            modifier,
            definition,
            action,
            mixin);
    }

    private static AbilityConfigNode Node(string type, string json)
    {
        using var doc = JsonDocument.Parse(json);

        return new AbilityConfigNode {
            Type = type,
            Values = doc.RootElement.EnumerateObject().ToDictionary(x => x.Name, x => x.Value.Clone())
        };
    }

    private sealed class RecordingForwarder : IInvokeForwarder
    {
        public List<Starlight.Protobuf.Core.IMessage> Messages { get; } = [];

        public Task Forward(IPlayer sender, ForwardType type, Starlight.Protobuf.Core.IMessage message, uint forwardPeer)
        {
            Assert.Equal(ForwardType.FORWARD_TYPE_TO_ALL, type);
            Messages.Add(message);
            return Task.CompletedTask;
        }
    }

    private static StarlightPlayer Player()
    {
        var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        var registry = new ModuleRegistry();
        registry.Build();
        var (_, server) = DirectTunnel.CreatePair();
        return new StarlightPlayer(services, registry, server) { Uid = 1 };
    }
}
