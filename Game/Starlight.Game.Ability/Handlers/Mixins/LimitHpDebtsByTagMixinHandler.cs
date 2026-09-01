using Starlight.Game.Ability.HpDebts;

namespace Starlight.Game.Ability.Handlers.Mixins;

public sealed class LimitHpDebtsByTagMixinHandler(HpDebtService debts) : AbilityMixinHandler
{
    public override ValueTask HandleAsync(AbilityContext context)
    {
        var target = context.Target ?? context.Source;

        if (context.Mixin is not null && context.Ability is not null)
            debts.SetLimit(context, target, context.Mixin, context.Ability);
        return ValueTask.CompletedTask;
    }
}
