using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Starlight.Game.Ability;
using Starlight.Game.Ability.Handlers.Actions;
using Starlight.Game.Modules;
using Starlight.Game.Player;
using Starlight.Game.Resources;
using Starlight.Game.Resources.Binary;
using Starlight.Game.Resources.Excel;
using Starlight.Protobuf.Core;
using Starlight.Protocol;
using Starlight.Rpc.Tunnel;
using Xunit;

namespace Starlight.Tests.Ability;

public sealed class ReviveElemEnergyResolutionTests
{
    [Fact]
    public void AvatarTalentParamList_FeedsModifyAbilityPlaceholders()
    {
        var data = new GameData(new ConfigurationBuilder().Build());

        data.AvatarTalentData[964] = new AvatarTalentData {
            Id = 964,
            ConfigName = "Arlecchino_Constellation_4",
            ParamList = [2f, 15f, 10f]
        };

        data.Talents["Arlecchino_Constellation_4"] = [
            new TalentConfigEntry {
                Type = "ModifyAbility",
                AbilityName = "Avatar_Arlecchino_ExtraAttack",
                ParamSpecial = "Constellation_4_ReviveEnergy",
                ParamDelta = Json("\"%2\"")
            }
        ];

        var component = new AbilityInitializer(data).RegisterAvatar(
            new AbilityScope(),
            new AbilityOwner(EntityId: 0x01000001, AbilityOwnerType.Avatar, PlayerUid: 1),
            avatarId: 0,
            skillDepotId: 0,
            sceneId: 0,
            new AvatarAbilitySources([964], PromoteLevel: 0));

        Assert.True(component.TryApplyTargetAbilitySpecial(
            AbilityKey.FromName("Avatar_Arlecchino_ExtraAttack"),
            AbilityKey.FromName("Constellation_4_ReviveEnergy"),
            value: 0f,
            out var result));
        Assert.Equal(expected: 15f, result);
    }

    [Fact]
    public async Task ReviveElemEnergy_LiteralValue_RemainsLiteral()
    {
        var component = Avatar();
        var ability = Ability(component, "LiteralEnergy", "{}");
        component.SetFightProperty((uint)FightProperty.FIGHT_PROP_MAX_FIRE_ENERGY, value: 60f);
        component.SetFightProperty((uint)FightProperty.FIGHT_PROP_CUR_FIRE_ENERGY, value: 0f);

        await new ReviveElemEnergyHandler(new RecordingForwarder()).HandleAsync(Context(
            component,
            ability,
            Node("ReviveElemEnergy", json: """{ "value": 5.0 }""")));

        Assert.Equal(expected: 5f, component.GetFightProperty((uint)FightProperty.FIGHT_PROP_CUR_FIRE_ENERGY));
    }

    [Fact]
    public async Task ReviveElemEnergy_NamedSpecial_AppliesTalentAdjustment()
    {
        var component = Avatar();

        var ability = Ability(component, "Avatar_Arlecchino_ExtraAttack", specials: """
                                                                                    { "Constellation_4_ReviveEnergy": 0 }
                                                                                    """);

        component.AddTargetAbilitySpecial(
            ability.Name,
            AbilityKey.FromName("Constellation_4_ReviveEnergy"),
            delta: 15f,
            ratio: 0f);
        component.SetFightProperty((uint)FightProperty.FIGHT_PROP_MAX_FIRE_ENERGY, value: 60f);
        component.SetFightProperty((uint)FightProperty.FIGHT_PROP_CUR_FIRE_ENERGY, value: 0f);

        await new ReviveElemEnergyHandler(new RecordingForwarder()).HandleAsync(Context(
            component,
            ability,
            Node("ReviveElemEnergy", json: """{ "value": "Constellation_4_ReviveEnergy" }""")));

        Assert.Equal(expected: 15f, component.GetFightProperty((uint)FightProperty.FIGHT_PROP_CUR_FIRE_ENERGY));
    }

    [Fact]
    public async Task ReviveElemEnergy_DynamicExpression_UsesRuntimeOverrideAndTalentSpecial()
    {
        var component = Avatar();

        var ability = Ability(component, "Navia_Talent_1_Driver", specials: """
                                                                            {
                                                                              "ReviveEnergy": 0,
                                                                              "NormalAttackDamage_Up_Stack": 0
                                                                            }
                                                                            """);

        component.AddTargetAbilitySpecial(
            ability.Name,
            AbilityKey.FromName("ReviveEnergy"),
            delta: 3f,
            ratio: 0f);

        await new SetOverrideMapValueHandler().HandleAsync(Context(
            component,
            ability,
            Node("SetOverrideMapValue", json: """
                                              { "overrideMapKey": "NormalAttackDamage_Up_Stack", "value": 2 }
                                              """)));

        component.SetFightProperty((uint)FightProperty.FIGHT_PROP_MAX_ROCK_ENERGY, value: 60f);
        component.SetFightProperty((uint)FightProperty.FIGHT_PROP_CUR_ROCK_ENERGY, value: 0f);

        await new ReviveElemEnergyHandler(new RecordingForwarder()).HandleAsync(Context(
            component,
            ability,
            Node("ReviveElemEnergy", json: """
                                           { "value": ["ReviveEnergy", "NormalAttackDamage_Up_Stack", "MUL"] }
                                           """)));

        Assert.Equal(expected: 6f, component.GetFightProperty((uint)FightProperty.FIGHT_PROP_CUR_ROCK_ENERGY));
    }

    private static AbilityComponent Avatar() =>
        new(new AbilityOwner(EntityId: 0x01000001, AbilityOwnerType.Avatar, AuthorityPeerId: 1, PlayerUid: 1));

    private static AbilityInstance Ability(AbilityComponent component, string name, string specials)
    {
        var definition = new AbilityConfig {
            AbilityName = name,
            AbilitySpecials = Json(specials)
        };
        return component.UpsertAbility(instancedAbilityId: 1, AbilityKey.FromName(name), definition: definition);
    }

    private static AbilityContext Context(AbilityComponent component, AbilityInstance ability, AbilityConfigNode action)
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
            Modifier: null,
            ability.Definition,
            action,
            Mixin: null);
    }

    private static AbilityConfigNode Node(string type, string json)
    {
        using var doc = JsonDocument.Parse(json);

        return new AbilityConfigNode {
            Type = type,
            Values = doc.RootElement.EnumerateObject().ToDictionary(x => x.Name, x => x.Value.Clone())
        };
    }

    private static JsonElement Json(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    private static StarlightPlayer Player()
    {
        var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        var registry = new ModuleRegistry();
        registry.Build();
        var (_, server) = DirectTunnel.CreatePair();
        return new StarlightPlayer(services, registry, server) { Uid = 1 };
    }

    private sealed class RecordingForwarder : IInvokeForwarder
    {
        public Task Forward(
            IPlayer sender,
            ForwardType type,
            IMessage message,
            uint forwardPeer
        ) => Task.CompletedTask;
    }
}
