using System.Text.Json;
using Starlight.Game.Ability.DynamicProps;
using Starlight.Game.Ability.HpDebts;
using Starlight.Game.Resources.Binary;

namespace Starlight.Game.Ability.Handlers.Actions;

public sealed class LoseHPHandler(IEnumerable<IAbilityDamageRuntime> runtimes) : AbilityActionHandler
{
    public override ValueTask HandleAsync(AbilityContext context) =>
        context.Action is {} action ? ExecuteAsync(context, action) : ValueTask.CompletedTask;

    private async ValueTask ExecuteAsync(AbilityContext context, AbilityConfigNode action)
    {
        if (context.Ability is null)
            return;

        var runtime = runtimes.LastOrDefault();

        if (runtime is null)
            return;

        var owner = AbilityRuntimeHelpers.AbilityOwnerOf(context);
        var target = context.Target ?? context.Source;
        var maxHp = target.GetFightProperty(AbilityFightProperty.MaxHp);
        var curHp = target.GetFightProperty(AbilityFightProperty.CurHp);

        if (!float.IsFinite(curHp) || curHp <= 0f)
            return;

        var amount =
            AbilityDynamicFloat.Get(context, action, "amount", owner) +
            AbilityDynamicFloat.Get(context, action, "amountByCasterMaxHPRatio", owner) * owner.GetFightProperty(AbilityFightProperty.MaxHp) +
            AbilityDynamicFloat.Get(context, action, "amountByCasterCurrentHPRatio", owner) *
            owner.GetFightProperty(AbilityFightProperty.CurHp) +
            AbilityDynamicFloat.Get(context, action, "amountByCasterAttackRatio", owner) *
            owner.GetFightProperty(AbilityFightProperty.CurAttack) +
            AbilityDynamicFloat.Get(context, action, "amountByTargetMaxHPRatio", owner) * maxHp +
            AbilityDynamicFloat.Get(context, action, "amountByTargetCurrentHPRatio", owner) * curHp;

        amount *= AbilityDynamicFloat.Get(context, action, "stackRatio", owner, defaultValue: 1f);

        var limboRatio = AbilityDynamicFloat.Get(context, action, "limboByTargetMaxHPRatio", owner);

        if (limboRatio > 1.192093e-07f)
            amount = Math.Min(amount, Math.Max(curHp - Math.Max(limboRatio * maxHp, val2: 1f), val2: 0f));

        if (!GetBool(action.Values, "lethal", defaultValue: true) && amount >= curHp)
            amount = Math.Max(curHp - 1f, val2: 0f);

        if (!float.IsFinite(amount) || amount <= 0f)
            return;

        await runtime.ApplyLoseHpAsync(
            context.Player,
            new AbilityDamageRequest(owner.Owner.EntityId, target.Owner.EntityId, amount));
    }

    private static bool GetBool(
        IReadOnlyDictionary<string, JsonElement> values,
        string key,
        bool defaultValue = false
    ) => values.TryGetValue(key, out var value) ?
        value.ValueKind == JsonValueKind.True :
        defaultValue;
}
