using Starlight.Game.Ability.DynamicProps;
using Starlight.Game.Resources.Binary;

namespace Starlight.Game.Ability.HpDebts;

public sealed class SwitchHealToHpDebtsProcessor(HpDebtService debts)
{
    public async ValueTask<bool> TryInterceptHealAsync(
        AbilityContext healContext,
        AbilityComponent target,
        float healAmount,
        string healTag
    )
    {
        var stopHeal = false;

        foreach (var modifier in target.AppliedModifiers.Values.ToArray())
        {
            if (!target.TryGetAbility(modifier.InstancedAbilityId, out var ability) ||
                ability.Definition is null ||
                modifier.ModifierName is not { Length: > 0 } modifierName ||
                !ability.Definition.Modifiers.TryGetValue(modifierName, out var modifierConfig))
                continue;

            foreach (var mixin in modifierConfig.ModifierMixins)
            {
                if (!string.Equals(mixin.Type, "SwitchHealToHPDebtsMixin", StringComparison.Ordinal))
                    continue;

                var context = healContext with {
                    Source = target,
                    Target = target,
                    Ability = ability,
                    Modifier = modifier,
                    Definition = ability.Definition,
                    Action = null,
                    Mixin = mixin
                };

                var handled = await ApplyAsync(context, target, mixin, ability, healAmount, healTag);
                stopHeal |= handled;
            }
        }

        return stopHeal;
    }

    public async ValueTask<bool> ApplyAsync(
        AbilityContext context,
        AbilityComponent target,
        AbilityConfigNode mixin,
        AbilityInstance ability,
        float healAmount,
        string healTag
    )
    {
        if (target.Owner.Type != AbilityOwnerType.Avatar || !AbilityPredicateEvaluator.Check(mixin, healTag))
            return false;

        var owner = AbilityRuntimeHelpers.AbilityOwnerOf(context with { Ability = ability });
        var ratio = AbilityDynamicFloat.Get(context with { Ability = ability }, mixin, "ratio", owner);
        await debts.ChangeAsync(context, target, healAmount * ratio, healTag);
        return true;
    }
}
