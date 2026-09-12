using Serilog;
using Starlight.Game.Ability.DynamicProps;
using Starlight.Game.Ability.HpDebts;
using Starlight.Game.Resources;
using Starlight.Protobuf.Core;
using Starlight.Protocol;

namespace Starlight.Game.Ability.Handlers.Actions;

public sealed class ReviveElemEnergyHandler(IInvokeForwarder forwarder) : AbilityActionHandler
{
    private static readonly (FightProperty Max, FightProperty Current)[] EnergyProperties = [
        (FightProperty.FIGHT_PROP_MAX_SPECIAL_ENERGY, FightProperty.FIGHT_PROP_CUR_SPECIAL_ENERGY),
        (FightProperty.FIGHT_PROP_MAX_FIRE_ENERGY, FightProperty.FIGHT_PROP_CUR_FIRE_ENERGY),
        (FightProperty.FIGHT_PROP_MAX_ELEC_ENERGY, FightProperty.FIGHT_PROP_CUR_ELEC_ENERGY),
        (FightProperty.FIGHT_PROP_MAX_WATER_ENERGY, FightProperty.FIGHT_PROP_CUR_WATER_ENERGY),
        (FightProperty.FIGHT_PROP_MAX_GRASS_ENERGY, FightProperty.FIGHT_PROP_CUR_GRASS_ENERGY),
        (FightProperty.FIGHT_PROP_MAX_WIND_ENERGY, FightProperty.FIGHT_PROP_CUR_WIND_ENERGY),
        (FightProperty.FIGHT_PROP_MAX_ICE_ENERGY, FightProperty.FIGHT_PROP_CUR_ICE_ENERGY),
        (FightProperty.FIGHT_PROP_MAX_ROCK_ENERGY, FightProperty.FIGHT_PROP_CUR_ROCK_ENERGY)
    ];

    public override async ValueTask HandleAsync(AbilityContext context)
    {
        if (context.Action is null || context.Ability is null)
            return;

        var target = context.Target ?? context.Source;

        if (target.Owner.Type != AbilityOwnerType.Avatar)
        {
            Log.Warning(
                "FAIL ReviveElemEnergy on {EntityId} - ERROR_TARGET_NOT_AVATAR",
                target.Owner.EntityId);
            return;
        }

        var owner = AbilityRuntimeHelpers.AbilityOwnerOf(context);
        var energyToRegen = AbilityDynamicFloat.Get(context, "value", owner);

        if (energyToRegen == 0f)
        {
            if (context.LogAbilitiesEnabled)
                Log.Debug(
                    "ReviveElemEnergy resolved to zero for {Ability} on {EntityId}",
                    context.Ability.Name,
                    target.Owner.EntityId);

            return;
        }

        if (!TryGetEnergyProperties(target, out var maxEnergyProperty, out var currentEnergyProperty))
        {
            Log.Warning(
                "FAIL ReviveElemEnergy on {EntityId} - ERROR_AVATAR_NO_ENERGY_SKILL_IN_DEPOT",
                target.Owner.EntityId);
            return;
        }

        var maxEnergy = target.GetFightProperty((uint)maxEnergyProperty);

        if (maxEnergy <= 0f)
        {
            Log.Warning(
                "FAIL ReviveElemEnergy on {EntityId} - ERROR_AVATAR_NO_ENERGY_SKILL_IN_DEPOT",
                target.Owner.EntityId);
            return;
        }

        var currentEnergy = target.GetFightProperty((uint)currentEnergyProperty);
        var newEnergy = Math.Clamp(currentEnergy + energyToRegen, min: 0f, maxEnergy);

        if (newEnergy == currentEnergy)
            return;

        target.SetFightProperty((uint)currentEnergyProperty, newEnergy);

        await Broadcast(
            context,
            new EntityFightPropUpdateNotify {
                EntityId = target.Owner.EntityId,
                FightPropMap = { [(uint)currentEnergyProperty] = newEnergy }
            });

        await Broadcast(
            context,
            new EntityFightPropChangeReasonNotify {
                EntityId = target.Owner.EntityId,
                PropType = (uint)currentEnergyProperty,
                PropDelta = newEnergy,
                Reason = PropChangeReason.PROP_CHANGE_REASON_ABILITY,
                ChangeEnergyReason = ChangeEnergyReason.CHANGE_ENERGY_REASON_NONE
            });
    }

    private Task Broadcast(AbilityContext context, IMessage message) =>
        forwarder.Forward(context.Player, ForwardType.FORWARD_TYPE_TO_ALL, message, forwardPeer: 0);

    private static bool TryGetEnergyProperties(
        AbilityComponent target,
        out FightProperty maxEnergyProperty,
        out FightProperty currentEnergyProperty
    )
    {
        foreach (var pair in EnergyProperties)
        {
            if (!target.FightProperties.ContainsKey((uint)pair.Max) &&
                !target.FightProperties.ContainsKey((uint)pair.Current))
                continue;

            maxEnergyProperty = pair.Max;
            currentEnergyProperty = pair.Current;
            return true;
        }

        maxEnergyProperty = FightProperty.FIGHT_PROP_NONE;
        currentEnergyProperty = FightProperty.FIGHT_PROP_NONE;
        return false;
    }
}
