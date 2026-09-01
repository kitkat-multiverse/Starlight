using Starlight.Game.Ability.DynamicProps;
using Starlight.Game.Ability.HpDebts;
using System.Text.Json;

namespace Starlight.Game.Ability.Handlers.Actions;

public sealed class HealHPHandler(
    HpDebtService debts,
    SwitchHealToHpDebtsProcessor switchHealToDebts
) : AbilityActionHandler
{
    public override async ValueTask HandleAsync(AbilityContext context)
    {
        if (context.Action is null || context.Ability is null)
            return;

        var owner = AbilityRuntimeHelpers.AbilityOwnerOf(context);
        var target = context.Target ?? context.Source;

        var casterAmount =
            AbilityDynamicFloat.Get(context, "amount", owner) +
            AbilityDynamicFloat.Get(context, "amountByCasterMaxHPRatio", owner) * owner.GetFightProperty(AbilityFightProperty.MaxHp) +
            AbilityDynamicFloat.Get(context, "amountByCasterCurrentHPRatio", owner) * owner.GetFightProperty(AbilityFightProperty.CurHp) +
            AbilityDynamicFloat.Get(context, "amountByCasterAttackRatio", owner) * owner.GetFightProperty(AbilityFightProperty.CurAttack);

        var ignoreAbilityProperty = GetBool(context.Action.Values, "ignoreAbilityProperty");
        var healRatio = AbilityDynamicFloat.Get(context, "healRatio", owner, defaultValue: 1f);

        var finalHeal = casterAmount *
                        (ignoreAbilityProperty ? 1f : 1f + target.GetFightProperty(AbilityFightProperty.HealAdd)) *
                        healRatio;

        var targetAmount =
            AbilityDynamicFloat.Get(context, "amountByTargetMaxHPRatio", owner) * target.GetFightProperty(AbilityFightProperty.MaxHp) +
            AbilityDynamicFloat.Get(context, "amountByTargetCurrentHPRatio", owner) * target.GetFightProperty(AbilityFightProperty.CurHp);

        finalHeal += targetAmount *
                     (1f + target.GetFightProperty(AbilityFightProperty.HealedAdd)) *
                     healRatio;

        if (finalHeal < 0f)
            return;

        var healTag = AbilityRuntimeHelpers.GetString(context.Action, "healTag");
        var muteHealEffect = GetBool(context.Action.Values, "muteHealEffect");

        // EntityAvatar performs these guards before GameEntity reaches the switch-heal mixin
        var curHp = target.GetFightProperty(AbilityFightProperty.CurHp);
        var maxHp = target.GetFightProperty(AbilityFightProperty.MaxHp);
        var curDebt = target.GetFightProperty(AbilityFightProperty.CurHpDebts);

        if (target.Owner.Type == AbilityOwnerType.Avatar && curHp <= 0f)
            return;

        if (target.Owner.Type == AbilityOwnerType.Avatar && curHp > 0f && target.IsKilled)
        {
            target.SetKilled(false);
            muteHealEffect = false;
        }

        if (curHp >= maxHp && curDebt <= 0f)
            return;

        // This interception must happen before ordinary debt repayment/HP mutation.
        // A handled SwitchHealToHPDebtsMixin suppresses the normal heal entirely.
        if (await switchHealToDebts.TryInterceptHealAsync(context, target, finalHeal, healTag))
            return;

        await debts.HealAsync(context, target, finalHeal, healTag, muteHealEffect);
    }

    private static bool GetBool(IReadOnlyDictionary<string, JsonElement> values, string key) =>
        values.TryGetValue(key, out var value) && value.ValueKind == JsonValueKind.True;
}
