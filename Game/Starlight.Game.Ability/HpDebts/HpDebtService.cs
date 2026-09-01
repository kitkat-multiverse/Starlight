using Google.Protobuf;
using Serilog;
using Starlight.Game.Ability.DynamicProps;
using Starlight.Game.Resources.Binary;
using Starlight.Protobuf.Registry;
using Starlight.Protocol;

namespace Starlight.Game.Ability.HpDebts;

// Credits go to PotRooms and Snoobi for making this possible <3
public sealed class HpDebtService(IInvokeForwarder forwarder, ProtocolRegistry protocol)
{
    public async ValueTask<HpDebtChange> ChangeAsync(
        AbilityContext context,
        AbilityComponent target,
        float amount,
        string hpDebtTag = ""
    )
    {
        if (target.Owner.Type != AbilityOwnerType.Avatar)
            return default;

        var current = target.GetFightProperty(AbilityFightProperty.CurHpDebts);
        var next = current + amount;
        var maximum = target.GetHpDebtMaximum(hpDebtTag);

        if (next < 0f)
            next = 0f;
        else if (next > maximum && maximum > 0f)
            next = maximum;

        var delta = next - current;
        target.SetFightProperty(AbilityFightProperty.CurHpDebts, next);

        Log.Warning("Set CurHpDebts to {Next} (was {Current}, delta {Delta})", next, current, delta);

        await BroadcastFightProp(context, target, AbilityFightProperty.CurHpDebts, next);

        if (delta == 0f)
            return new HpDebtChange(current, next, delta, ChangeHpDebts.CHANGE_HP_DEBTS_NONE);

        var reason = next == 0f ? ChangeHpDebts.CHANGE_HP_DEBTS_PAY_FINISH
            : delta > 0f ? ChangeHpDebts.CHANGE_HP_DEBTS_ADD_ABILITY
            : ChangeHpDebts.CHANGE_HP_DEBTS_REDUCE_ABILITY;

        await BroadcastDebtReason(context, target, delta, reason);
        return new HpDebtChange(current, next, delta, reason);
    }

    public void SetLimit(
        AbilityContext context,
        AbilityComponent target,
        AbilityConfigNode mixin,
        AbilityInstance ability
    )
    {
        if (target.Owner.Type != AbilityOwnerType.Avatar)
            return;

        var owner = AbilityRuntimeHelpers.AbilityOwnerOf(context with { Ability = ability });
        var ratio = AbilityDynamicFloat.Get(context with { Ability = ability }, mixin, "maxHpDebtRatio", owner);
        target.SetHpDebtLimit(ratio, GetStringArray(mixin, "hpDebtTags"));

        if (context.Modifier is not null)
            target.MarkHpDebtLimitModifier(context.Modifier.InstancedModifierId);
    }

    public void OnModifierAdded(
        AbilityContext context,
        AbilityComponent target,
        AbilityModifierInstance modifier,
        AbilityInstance? ability = null
    )
    {
        if (target.Owner.Type != AbilityOwnerType.Avatar)
            return;

        ability ??= target.TryGetAbility(modifier.InstancedAbilityId, out var resolved) ? resolved : null;

        if (ability?.Definition is null ||
            modifier.ModifierName is not { Length: > 0 } modifierName ||
            !ability.Definition.Modifiers.TryGetValue(modifierName, out var config))
            return;

        foreach (var mixin in config.ModifierMixins)
        {
            if (!string.Equals(mixin.Type, "LimitHpDebtsByTagMixin", StringComparison.Ordinal))
                continue;

            SetLimit(
                context with {
                    Source = target,
                    Target = target,
                    Ability = ability,
                    Modifier = modifier,
                    Definition = ability.Definition,
                    Action = null,
                    Mixin = mixin
                },
                target,
                mixin,
                ability);
        }
    }

    public async ValueTask<float> HealAsync(
        AbilityContext context,
        AbilityComponent target,
        float amount,
        string healTag,
        bool muteHealEffect
    )
    {
        var curHp = target.GetFightProperty(AbilityFightProperty.CurHp);
        var maxHp = target.GetFightProperty(AbilityFightProperty.MaxHp);
        var curDebt = target.GetFightProperty(AbilityFightProperty.CurHpDebts);

        if (target.Owner.Type == AbilityOwnerType.Avatar && curHp <= 0f)
            return 0f;

        // If the entity has positive HP but stale killed state, recover it and force a non-muted reason packet for the heal
        if (target.Owner.Type == AbilityOwnerType.Avatar && curHp > 0f && target.IsKilled)
        {
            target.SetKilled(false);
            muteHealEffect = false;
        }

        if (curHp >= maxHp && curDebt <= 0f)
            return 0f;

        var repay = Math.Min(amount, curDebt);
        var heal = Math.Min(maxHp - curHp, amount - repay);

        target.SetFightProperty(AbilityFightProperty.CurHp, curHp + heal);
        target.SetFightProperty(AbilityFightProperty.CurHpDebts, curDebt - repay);

        if (repay > 0f)
        {
            await BroadcastFightProp(
                context,
                target,
                AbilityFightProperty.CurHpDebts,
                target.GetFightProperty(AbilityFightProperty.CurHpDebts));

            target.SetFightProperty(AbilityFightProperty.CurHpPaidDebts, repay);
            await BroadcastFightProp(context, target, AbilityFightProperty.CurHpPaidDebts, repay);

            var debtReason = target.GetFightProperty(AbilityFightProperty.CurHpDebts) > 0f ?
                ChangeHpDebts.CHANGE_HP_DEBTS_REDUCE_ABILITY :
                ChangeHpDebts.CHANGE_HP_DEBTS_PAY_FINISH;
            await BroadcastDebtReason(context, target, -repay, debtReason);

            target.SetFightProperty(AbilityFightProperty.CurHpPaidDebts, value: 0f);
            await BroadcastFightProp(context, target, AbilityFightProperty.CurHpPaidDebts, value: 0f);
        }

        if (heal > 0f)
        {
            await BroadcastFightProp(
                context,
                target,
                AbilityFightProperty.CurHp,
                target.GetFightProperty(AbilityFightProperty.CurHp));
        }

        if (heal > 0f || repay > 0f)
            await BroadcastBeingHealed(context, target, heal + repay, healTag);

        if (target.Owner.Type == AbilityOwnerType.Avatar && heal > 0f)
        {
            await Broadcast(
                context,
                new EntityFightPropChangeReasonNotify {
                    EntityId = target.Owner.EntityId,
                    PropType = AbilityFightProperty.CurHp,
                    PropDelta = heal,
                    Reason = muteHealEffect ? PropChangeReason.PROP_CHANGE_REASON_NONE : PropChangeReason.PROP_CHANGE_REASON_ABILITY,
                    ChangeHpReason = ChangeHpReason.CHANGE_HP_REASON_ADD_ABILITY
                });
        }

        return heal;
    }

    internal Task BroadcastFightProp(
        AbilityContext context,
        AbilityComponent target,
        uint property,
        float value
    ) =>
        Broadcast(
            context,
            new EntityFightPropUpdateNotify {
                EntityId = target.Owner.EntityId,
                FightPropMap = { [property] = value }
            });

    private Task BroadcastDebtReason(
        AbilityContext context,
        AbilityComponent target,
        float delta,
        ChangeHpDebts reason
    ) =>
        Broadcast(
            context,
            new EntityFightPropChangeReasonNotify {
                EntityId = target.Owner.EntityId,
                PropType = AbilityFightProperty.CurHpDebts,
                PropDelta = delta,
                PaidHpDebts = delta < 0f ? -delta : 0f,
                ChangeHpReason = delta < 0f ? ChangeHpReason.CHANGE_HP_REASON_ADD_ABILITY : ChangeHpReason.CHANGE_HP_REASON_NONE,
                Reason = PropChangeReason.PROP_CHANGE_REASON_ABILITY,
                ChangeHpDebts = reason
            });

    private Task BroadcastBeingHealed(
        AbilityContext context,
        AbilityComponent target,
        float amount,
        string healTag
    )
    {
        var beingHealed = new EvtBeingHealedNotify {
            TargetId = target.Owner.EntityId,
            SourceId = target.Owner.EntityId,
            HealAmount = amount,
            HealTag = healTag
        };

        var invoke = new CombatInvokeEntry {
            ArgumentType = CombatTypeArgument.COMBAT_TYPE_ARGUMENT_BEING_HEALED_NTF,
            ForwardType = ForwardType.FORWARD_TYPE_TO_ALL,
            CombatData = ByteString.CopyFrom(protocol.Serialize(beingHealed))
        };

        return Broadcast(context, new CombatInvocationsNotify { InvokeList = { invoke } });
    }

    private Task Broadcast(AbilityContext context, Starlight.Protobuf.Core.IMessage message) =>
        forwarder.Forward(context.Player, ForwardType.FORWARD_TYPE_TO_ALL, message, forwardPeer: 0);

    private static IEnumerable<string> GetStringArray(AbilityConfigNode node, string field)
    {
        if (!node.Values.TryGetValue(field, out var value) || value.ValueKind != System.Text.Json.JsonValueKind.Array)
            return [];

        return value.EnumerateArray()
            .Where(item => item.ValueKind == System.Text.Json.JsonValueKind.String)
            .Select(item => item.GetString() ?? string.Empty)
            .Where(item => item.Length != 0)
            .ToArray();
    }
}

public readonly record struct HpDebtChange(
    float Previous,
    float Current,
    float Delta,
    ChangeHpDebts Reason
);
