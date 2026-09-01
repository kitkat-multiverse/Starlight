using Starlight.Game.Ability.DynamicProps;
using Starlight.Game.Ability.HpDebts;

namespace Starlight.Game.Ability.Handlers.Actions;

public abstract class HpDebtActionHandler(HpDebtService debts, float multiplier) : AbilityActionHandler
{
    public override async ValueTask HandleAsync(AbilityContext context)
    {
        var target = context.Target ?? context.Source;

        if (target.Owner.Type != AbilityOwnerType.Avatar ||
            context.Action is null ||
            !context.Action.Values.TryGetValue("value", out var debtValue))
            return;

        var owner = AbilityRuntimeHelpers.AbilityOwnerOf(context);
        var debt = AbilityDynamicFloat.Get(context, debtValue, owner);

        var tag = AbilityRuntimeHelpers.GetString(
            context.Action,
            "hpDebtTag",
            // 7.0 resources have this field obfuscated.
            "HDOCNDAFLIJ");

        await debts.ChangeAsync(context, target, debt * multiplier, tag);
    }
}
